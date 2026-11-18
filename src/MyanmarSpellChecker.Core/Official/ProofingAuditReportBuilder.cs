// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Official
{
    public enum ProofingReportExportFormat
    {
        FriendlyExcelWorkbook = 0,
        FriendlyCsvSpreadsheet = 1,
        NativeOfficeDocument = 2,
        InteractiveHtmlAndPdf = 3,
        CopySummaryToClipboard = 4,
        PrintableA4WordDocument = 5,
        ExecutivePowerPointDeck = 6
    }

    public sealed class ProofingAuditSectionBreakdown
    {
        public string SectionName { get; set; } = string.Empty;
        public int ConfirmedWrongCount { get; set; }
        public int SuspiciousReviewCount { get; set; }
        public int TotalIssues => ConfirmedWrongCount + SuspiciousReviewCount;
    }

    public sealed class ProofingAuditItemInput
    {
        public string LocationLabel { get; set; } = string.Empty;
        public string PageOrSheetLabel { get; set; } = string.Empty;
        public string ParagraphOrCellLabel { get; set; } = string.Empty;
        public string LineLabel { get; set; } = string.Empty;
        public string ContextSnippet { get; set; } = string.Empty;
        public ProofingIssue Issue { get; set; } = new ProofingIssue();
        public string? ChosenReplacementOverride { get; set; }

        public ProofingAuditItemInput()
        {
        }

        public ProofingAuditItemInput(
            string locationLabel,
            string contextSnippet,
            ProofingIssue issue,
            string? chosenReplacementOverride = null,
            string? pageOrSheetLabel = null,
            string? paragraphOrCellLabel = null,
            string? lineLabel = null)
        {
            LocationLabel = locationLabel ?? string.Empty;
            PageOrSheetLabel = pageOrSheetLabel ?? string.Empty;
            ParagraphOrCellLabel = paragraphOrCellLabel ?? string.Empty;
            LineLabel = lineLabel ?? string.Empty;
            ContextSnippet = contextSnippet ?? string.Empty;
            Issue = issue ?? new ProofingIssue();
            ChosenReplacementOverride = chosenReplacementOverride;
        }
    }

    public sealed class ProofingSessionFixedRecord
    {
        public string LocationLabel { get; set; } = string.Empty;
        public string OriginalText { get; set; } = string.Empty;
        public string VisualOriginalText { get; set; } = string.Empty;
        public string AppliedReplacement { get; set; } = string.Empty;
        public DateTime AppliedAtLocal { get; set; } = DateTime.Now;

        public ProofingSessionFixedRecord()
        {
        }

        public ProofingSessionFixedRecord(
            string locationLabel,
            string originalText,
            string appliedReplacement,
            DateTime? appliedAtLocal = null)
        {
            LocationLabel = locationLabel ?? string.Empty;
            OriginalText = originalText ?? string.Empty;
            AppliedReplacement = appliedReplacement ?? string.Empty;
            AppliedAtLocal = appliedAtLocal ?? DateTime.Now;
            var tempIssue = new ProofingIssue
            {
                OriginalText = OriginalText,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = AppliedReplacement, Score = 100.0 }
                }
            };
            VisualOriginalText = tempIssue.FormatVisualOriginalText();
        }
    }

    public sealed class ProofingRepeatedWordGroup
    {
        public string CanonicalKey { get; set; } = string.Empty;
        public string VisualOriginal { get; set; } = string.Empty;
        public string RecommendedFix { get; set; } = string.Empty;
        public bool IsConfirmedWrong { get; set; }
        public string StatusBadge { get; set; } = string.Empty;
        public int Count { get; set; }
        public string SampleLocations { get; set; } = string.Empty;
        public string PrimaryReason { get; set; } = string.Empty;
    }

    public sealed class ProofingAuditItemRow
    {
        public int Index { get; set; }
        public int DocumentOrderIndex { get; set; }
        public bool IsConfirmedWrong { get; set; }
        public string StatusBadge { get; set; } = string.Empty;
        public string CategoryLabel { get; set; } = string.Empty;
        public string LocationLabel { get; set; } = string.Empty;
        public string PageOrSheetLabel { get; set; } = string.Empty;
        public string ParagraphOrCellLabel { get; set; } = string.Empty;
        public string LineLabel { get; set; } = string.Empty;
        public string RawOriginal { get; set; } = string.Empty;
        public string VisualOriginal { get; set; } = string.Empty;
        public string RecommendedFix { get; set; } = string.Empty;
        public string AlternativeFixes { get; set; } = string.Empty;
        public string HowItShouldBeDisplay { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string ContextSnippet { get; set; } = string.Empty;
        public string MarkedContextSnippet { get; set; } = string.Empty;
    }

    public sealed class ProofingAuditReportModel
    {
        public string DocumentTitle { get; set; } = string.Empty;
        public string HostAppLabel { get; set; } = "Microsoft Word";
        public string StyleModeLabel { get; set; } = string.Empty;
        public DateTime GeneratedAtLocal { get; set; } = DateTime.Now;
        public string OfficialDateHeader { get; set; } = string.Empty;

        // 1. Document Size & Accuracy Scorecard
        public int TotalWords { get; set; }
        public int MyanmarSyllables { get; set; }
        public int ParagraphOrCellCount { get; set; }
        public int SentenceCount { get; set; }
        public int EstimatedCleanWords { get; set; }
        public double CleanAccuracyPercent { get; set; } = 100.0;
        public double ConfirmedWrongRatePercent { get; set; }
        public double SuspiciousRatePercent { get; set; }
        public int OverallScore { get; set; } = 100;
        public string GradeCode { get; set; } = "A+";
        public string LocalizedGradeLabel { get; set; } = string.Empty;
        public double UnicodePurityPercent { get; set; } = 100.0;
        public double FormalRegisterPercent { get; set; } = 100.0;

        // 2. Bucket 1 — Confirmed Wrong (High-Certainty Rules)
        public int ConfirmedWrongCount { get; set; }
        public int ConfirmedSpellingCount { get; set; }
        public int ConfirmedEncodingAndLookalikeCount { get; set; }
        public int ConfirmedGrammarAndStyleCount { get; set; }

        // 3. Bucket 2 — Suspicious / Human Review Needed
        public int SuspiciousReviewCount { get; set; }
        public int SuspiciousNameOrOovCount { get; set; }
        public int SuspiciousContextMeaningCount { get; set; }
        public int ConsistencyWarningCount { get; set; }
        public List<string> ConsistencySummaries { get; set; } = new List<string>();

        // 4. Bucket 3 — Already Fixed in This Session
        public int SessionFixedCount => SessionFixedRecords.Count;
        public List<ProofingSessionFixedRecord> SessionFixedRecords { get; set; } = new List<ProofingSessionFixedRecord>();

        // Section A & Section B Tables
        public int TotalUniqueGroupsCount { get; set; }
        public List<ProofingAuditSectionBreakdown> SectionBreakdowns { get; set; } = new List<ProofingAuditSectionBreakdown>();
        public List<ProofingRepeatedWordGroup> TopRepeatedGroups { get; set; } = new List<ProofingRepeatedWordGroup>();
        public List<ProofingRepeatedWordGroup> AllRepeatedGroups { get; set; } = new List<ProofingRepeatedWordGroup>();
        public List<ProofingAuditItemRow> AllItemsInDocumentOrder { get; set; } = new List<ProofingAuditItemRow>();
        public List<ProofingAuditItemRow> ConfirmedWrongItems { get; set; } = new List<ProofingAuditItemRow>();
        public List<ProofingAuditItemRow> SuspiciousReviewItems { get; set; } = new List<ProofingAuditItemRow>();
    }

    /// <summary>
    /// Builds structured 4-part Proofing Audit Reports separating Confirmed Wrong (high-certainty rules),
    /// Suspicious / Review Needed (names, OOV guesses, contextual meaning checks, inconsistencies),
    /// and Already Fixed in Session records — with "How It Should Be" tables, clipboard summaries,
    /// and 100% offline interactive print-to-PDF HTML reports.
    /// </summary>
    public static class ProofingAuditReportBuilder
    {
        public static ProofingAuditReportModel Build(
            string? documentTitle,
            string hostAppLabel,
            ProofingStyleMode styleMode,
            DocumentQualityReport? qualityReport,
            IReadOnlyList<ProofingAuditItemInput>? unresolvedInputs,
            IReadOnlyList<ProofingSessionFixedRecord>? sessionFixedRecords = null,
            DateTime? nowOverride = null,
            IReadOnlyList<string>? allOrderedSectionNames = null)
        {
            DateTime now = nowOverride ?? DateTime.Now;
            MyanmarDateInfo mmDate = MyanmarTraditionalCalendar.FromGregorian(now);
            var inputs = unresolvedInputs ?? Array.Empty<ProofingAuditItemInput>();
            var fixedList = sessionFixedRecords != null
                ? sessionFixedRecords.ToList()
                : new List<ProofingSessionFixedRecord>();

            var model = new ProofingAuditReportModel
            {
                DocumentTitle = string.IsNullOrWhiteSpace(documentTitle)
                    ? UiLocalizer.Pick("Untitled Document", "အမည်မသိ စာတမ်း")
                    : documentTitle!.Trim(),
                HostAppLabel = string.IsNullOrWhiteSpace(hostAppLabel) ? "Microsoft Office" : hostAppLabel,
                StyleModeLabel = styleMode == ProofingStyleMode.FormalOfficial
                    ? UiLocalizer.Pick("Official / Literary (ရုံးသုံး/စာပေစနစ်)", "စာပေ / ရုံးသုံးစနစ် (Official / Literary)")
                    : (styleMode == ProofingStyleMode.ColloquialPermissive
                        ? UiLocalizer.Pick("Casual / Web (အရပ်သုံး စကားပြောစနစ်)", "အရပ်သုံး စကားပြောစနစ် (Casual / Web)")
                        : UiLocalizer.Pick("General Standard (အထွေထွေ စံသတ်ပုံ)", "အထွေထွေ စံသတ်ပုံ (General Standard)")),
                GeneratedAtLocal = now,
                OfficialDateHeader = $"{now:yyyy-MM-dd HH:mm} • {mmDate.OfficialGregorianDateText} ({mmDate.OfficialMyanmarDateText})"
            };

            if (qualityReport != null)
            {
                model.TotalWords = Math.Max(qualityReport.WordCount, inputs.Count + fixedList.Count);
                model.MyanmarSyllables = qualityReport.MyanmarSyllableCount;
                model.ParagraphOrCellCount = qualityReport.ParagraphCount;
                model.SentenceCount = qualityReport.SentenceCount;
                model.OverallScore = qualityReport.OverallScore;
                model.GradeCode = qualityReport.GradeCode;
                model.LocalizedGradeLabel = qualityReport.LocalizedGradeLabel;
                model.UnicodePurityPercent = qualityReport.UnicodePurityPercent;
                model.FormalRegisterPercent = qualityReport.FormalRegisterPercent;
                model.ConsistencyWarningCount = qualityReport.ConsistencyWarningCount;
                if (qualityReport.ConsistencySummaries != null)
                {
                    model.ConsistencySummaries.AddRange(qualityReport.ConsistencySummaries);
                }
            }
            else
            {
                model.TotalWords = Math.Max(1, inputs.Count + fixedList.Count);
                model.GradeCode = "A";
                model.LocalizedGradeLabel = "A";
            }

            int wrongIdx = 1;
            int suspIdx = 1;
            var groupBuckets = new Dictionary<string, (ProofingAuditItemInput Sample, bool Confirmed, int Count, List<string> Locs)>(StringComparer.Ordinal);

            for (int i = 0; i < inputs.Count; i++)
            {
                var item = inputs[i];
                ProofingIssue iss = item.Issue ?? new ProofingIssue();
                bool isConfirmedWrong = iss.IsHighCertaintyPrecheck && !iss.IsContextualWarning;

                string primaryFix = !string.IsNullOrWhiteSpace(item.ChosenReplacementOverride)
                    ? item.ChosenReplacementOverride!
                    : (iss.Suggestions != null && iss.Suggestions.Count > 0 ? iss.Suggestions[0].Replacement : string.Empty);

                var altList = new List<string>();
                if (iss.Suggestions != null && iss.Suggestions.Count > 1)
                {
                    for (int s = 0; s < Math.Min(iss.Suggestions.Count, 4); s++)
                    {
                        string cand = iss.Suggestions[s].Replacement ?? string.Empty;
                        if (!string.IsNullOrEmpty(cand) && !string.Equals(cand, primaryFix, StringComparison.Ordinal))
                        {
                            altList.Add(cand);
                        }
                    }
                }

                string altDisplay = altList.Count > 0 ? string.Join(" / ", altList) : string.Empty;
                string howShouldBe;
                if (!string.IsNullOrEmpty(primaryFix))
                {
                    howShouldBe = altList.Count > 0
                        ? $"{primaryFix}   ({UiLocalizer.Pick("Alt:", "အခြား -")} {altDisplay})"
                        : primaryFix;
                }
                else
                {
                    howShouldBe = UiLocalizer.Pick("(Inspect manually)", "(လူကိုယ်တိုင် စိစစ်ရန်)");
                }

                string visOrig = iss.FormatVisualOriginalText(primaryFix);
                string rawCtxClean = (item.ContextSnippet ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
                string cleanCtx = rawCtxClean.Length > 140
                    ? MyanmarSyllableBreaker.TruncateAtSyllableBoundary(rawCtxClean, 140, "…")
                    : rawCtxClean;
                string markedCtx = BuildMarkedContextSnippet(rawCtxClean, iss);

                ResolveStructuredLocationFields(
                    item,
                    out string pageOrSheet,
                    out string paraOrCell,
                    out string lineLbl);

                string categoryLabel = GetCategoryLabel(iss, isConfirmedWrong);
                string statusBadge = isConfirmedWrong
                    ? UiLocalizer.Pick("Confirmed Wrong (သေချာအမှား)", "သေချာအမှား (Confirmed Wrong)")
                    : (iss.IsContextualWarning
                        ? UiLocalizer.Pick("Review Needed • Meaning (အနက်သံသယ)", "စိစစ်ရန် • အနက်သံသယ (Review)")
                        : UiLocalizer.Pick("Review Needed (စိစစ်ရန်)", "စိစစ်ရန် • အမည်/ခန့်မှန်း (Review)"));

                var row = new ProofingAuditItemRow
                {
                    Index = isConfirmedWrong ? wrongIdx++ : suspIdx++,
                    DocumentOrderIndex = model.AllItemsInDocumentOrder.Count + 1,
                    IsConfirmedWrong = isConfirmedWrong,
                    StatusBadge = statusBadge,
                    CategoryLabel = categoryLabel,
                    LocationLabel = item.LocationLabel ?? string.Empty,
                    PageOrSheetLabel = pageOrSheet,
                    ParagraphOrCellLabel = paraOrCell,
                    LineLabel = lineLbl,
                    RawOriginal = iss.OriginalText ?? string.Empty,
                    VisualOriginal = visOrig,
                    RecommendedFix = primaryFix,
                    AlternativeFixes = altDisplay,
                    HowItShouldBeDisplay = howShouldBe,
                    Explanation = iss.Message ?? string.Empty,
                    ContextSnippet = cleanCtx,
                    MarkedContextSnippet = markedCtx
                };
                model.AllItemsInDocumentOrder.Add(row);

                if (isConfirmedWrong)
                {
                    model.ConfirmedWrongItems.Add(row);
                    model.ConfirmedWrongCount++;

                    if (iss.IssueType == ProofingIssueType.LookalikeCharacterError ||
                        iss.IssueType == ProofingIssueType.ZawgyiEncodingError ||
                        !string.IsNullOrEmpty(iss.FormatVisualDiffBadge(primaryFix)))
                    {
                        model.ConfirmedEncodingAndLookalikeCount++;
                    }
                    else if (iss.IsGrammarOrStyle)
                    {
                        model.ConfirmedGrammarAndStyleCount++;
                    }
                    else
                    {
                        model.ConfirmedSpellingCount++;
                    }
                }
                else
                {
                    model.SuspiciousReviewItems.Add(row);
                    model.SuspiciousReviewCount++;

                    if (iss.IsContextualWarning)
                        model.SuspiciousContextMeaningCount++;
                    else
                        model.SuspiciousNameOrOovCount++;
                }

                string groupKey = UnicodeNormalizer.NormalizeCanonicalOrder((iss.OriginalText ?? string.Empty).Trim()) +
                                  "|" + (isConfirmedWrong ? "W" : "S");
                if (!string.IsNullOrWhiteSpace(iss.OriginalText))
                {
                    string locTrim = (item.LocationLabel ?? string.Empty).Trim();
                    if (!groupBuckets.TryGetValue(groupKey, out var grp))
                    {
                        var locs = new List<string>(4);
                        if (locTrim.Length > 0)
                            locs.Add(locTrim);
                        groupBuckets[groupKey] = (item, isConfirmedWrong, 1, locs);
                    }
                    else
                    {
                        if (locTrim.Length > 0 && grp.Locs.Count < 4 && !grp.Locs.Contains(locTrim))
                            grp.Locs.Add(locTrim);
                        groupBuckets[groupKey] = (grp.Sample, grp.Confirmed, grp.Count + 1, grp.Locs);
                    }
                }
            }

            model.SessionFixedRecords = fixedList;

            int denomWords = Math.Max(1, model.TotalWords);
            int totalFlagged = model.ConfirmedWrongCount + model.SuspiciousReviewCount;
            model.EstimatedCleanWords = Math.Max(0, denomWords - totalFlagged);
            model.ConfirmedWrongRatePercent = Math.Min(100.0, Math.Round((model.ConfirmedWrongCount * 100.0) / denomWords, 2));
            model.SuspiciousRatePercent = Math.Min(100.0, Math.Round((model.SuspiciousReviewCount * 100.0) / denomWords, 2));
            model.CleanAccuracyPercent = Math.Max(0.0, Math.Round(100.0 - model.ConfirmedWrongRatePercent - model.SuspiciousRatePercent, 2));

            model.TotalUniqueGroupsCount = groupBuckets.Count;
            var allSortedGroups = groupBuckets
                .OrderByDescending(kv => kv.Value.Count)
                .ThenByDescending(kv => kv.Value.Confirmed)
                .Take(500)
                .Select(kv =>
                {
                    var g = kv.Value;
                    var sampleIss = g.Sample.Issue;
                    string rec = !string.IsNullOrWhiteSpace(g.Sample.ChosenReplacementOverride)
                        ? g.Sample.ChosenReplacementOverride!
                        : (sampleIss.Suggestions?.FirstOrDefault()?.Replacement ?? UiLocalizer.Pick("(Review)", "(စိစစ်ရန်)"));
                    string locStr = string.Join(", ", g.Locs);
                    if (g.Count > g.Locs.Count)
                        locStr += $" (+{g.Count - g.Locs.Count})";

                    return new ProofingRepeatedWordGroup
                    {
                        CanonicalKey = kv.Key,
                        VisualOriginal = sampleIss.FormatVisualOriginalText(rec),
                        RecommendedFix = rec,
                        IsConfirmedWrong = g.Confirmed,
                        StatusBadge = g.Confirmed
                            ? UiLocalizer.Pick("Confirmed Wrong", "သေချာအမှား")
                            : UiLocalizer.Pick("Suspicious / Review", "သံသယ / စိစစ်ရန်"),
                        Count = g.Count,
                        SampleLocations = locStr,
                        PrimaryReason = sampleIss.Message ?? string.Empty
                    };
                })
                .ToList();

            model.AllRepeatedGroups = allSortedGroups;
            model.TopRepeatedGroups = allSortedGroups.Take(30).ToList();

            // Build Per-Sheet / Per-Slide / Per-Page Breakdown across ALL sections (including clean 0-issue sheets/slides!)
            var secMap = new Dictionary<string, ProofingAuditSectionBreakdown>(StringComparer.OrdinalIgnoreCase);
            var secOrder = new List<string>();
            if (allOrderedSectionNames != null)
            {
                for (int s = 0; s < allOrderedSectionNames.Count; s++)
                {
                    string secName = (allOrderedSectionNames[s] ?? string.Empty).Trim();
                    if (secName.Length > 0 && !secMap.ContainsKey(secName))
                    {
                        var bd = new ProofingAuditSectionBreakdown { SectionName = secName };
                        secMap[secName] = bd;
                        secOrder.Add(secName);
                    }
                }
            }

            for (int i = 0; i < model.AllItemsInDocumentOrder.Count; i++)
            {
                var r = model.AllItemsInDocumentOrder[i];
                string secKey = !string.IsNullOrWhiteSpace(r.PageOrSheetLabel)
                    ? r.PageOrSheetLabel.Trim()
                    : UiLocalizer.Pick("Main Content", "ပင်မစာသား");
                if (!secMap.TryGetValue(secKey, out var bd))
                {
                    bd = new ProofingAuditSectionBreakdown { SectionName = secKey };
                    secMap[secKey] = bd;
                    secOrder.Add(secKey);
                }
                if (r.IsConfirmedWrong)
                    bd.ConfirmedWrongCount++;
                else
                    bd.SuspiciousReviewCount++;
            }

            if (secOrder.Count == 0)
            {
                string fallbackSec = UiLocalizer.Pick("Full Document", "စာတမ်းတစ်ခုလုံး");
                secMap[fallbackSec] = new ProofingAuditSectionBreakdown { SectionName = fallbackSec };
                secOrder.Add(fallbackSec);
            }

            for (int s = 0; s < secOrder.Count; s++)
            {
                model.SectionBreakdowns.Add(secMap[secOrder[s]]);
            }

            return model;
        }

        private static void ResolveStructuredLocationFields(
            ProofingAuditItemInput item,
            out string pageOrSheet,
            out string paraOrCell,
            out string lineLbl)
        {
            pageOrSheet = (item.PageOrSheetLabel ?? string.Empty).Trim();
            paraOrCell = (item.ParagraphOrCellLabel ?? string.Empty).Trim();
            lineLbl = (item.LineLabel ?? string.Empty).Trim();

            if (pageOrSheet.Length > 0 || paraOrCell.Length > 0 || lineLbl.Length > 0)
                return;

            string loc = (item.LocationLabel ?? string.Empty).Trim();
            if (loc.Length == 0)
                return;

            // PowerPoint pattern: "Slide 2 - Table [R2,C3]" or "ဆလိုက် 2 - Title 1" or "Master:Theme - #9"
            int dashIdx = loc.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx > 0 && dashIdx < loc.Length - 3 &&
                (loc.StartsWith("Slide ", StringComparison.OrdinalIgnoreCase) ||
                 loc.StartsWith("ဆလိုက် ", StringComparison.Ordinal) ||
                 loc.StartsWith("Master:", StringComparison.OrdinalIgnoreCase) ||
                 loc.StartsWith("Layout:", StringComparison.OrdinalIgnoreCase) ||
                 loc.StartsWith("မူရင်းပုံစံ", StringComparison.Ordinal)))
            {
                pageOrSheet = loc.Substring(0, dashIdx).Trim();
                paraOrCell = loc.Substring(dashIdx + 3).Trim();
                int lineCount = 1;
                string snap = item.ContextSnippet ?? string.Empty;
                int limit = Math.Min(snap.Length, Math.Max(0, item.Issue?.StartIndex ?? 0));
                for (int i = 0; i < limit; i++)
                {
                    if (snap[i] == '\n' || snap[i] == '\r' || snap[i] == '\v')
                        lineCount++;
                }
                lineLbl = UiLocalizer.Pick($"Line {lineCount}", $"လိုင်း {lineCount}");
                return;
            }

            // Excel pattern: "Sheet1!C14"
            int bangIdx = loc.IndexOf('!');
            if (bangIdx > 0 && bangIdx < loc.Length - 1)
            {
                pageOrSheet = loc.Substring(0, bangIdx).Trim();
                paraOrCell = UiLocalizer.Pick($"Cell {loc.Substring(bangIdx + 1).Trim()}", $"အကွက် {loc.Substring(bangIdx + 1).Trim()}");
                int lineCount = 1;
                string snap = item.ContextSnippet ?? string.Empty;
                int limit = Math.Min(snap.Length, Math.Max(0, item.Issue?.StartIndex ?? 0));
                for (int i = 0; i < limit; i++)
                {
                    if (snap[i] == '\n')
                        lineCount++;
                }
                lineLbl = UiLocalizer.Pick($"Line {lineCount}", $"လိုင်း {lineCount}");
                return;
            }

            // Word / Bullet pattern: "Page 1 • Paragraph 3, Line 3" or "Table • Para #39"
            string[] parts = loc.Split(new[] { '•' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                pageOrSheet = parts[0].Trim();
                string tail = string.Join(" • ", parts.Skip(1).Select(p => p.Trim()));
                int commaLine = tail.LastIndexOf(',');
                if (commaLine > 0 && commaLine < tail.Length - 1)
                {
                    string maybeLine = tail.Substring(commaLine + 1).Trim();
                    if (maybeLine.StartsWith("Line", StringComparison.OrdinalIgnoreCase) ||
                        maybeLine.StartsWith("လိုင်း", StringComparison.Ordinal))
                    {
                        paraOrCell = tail.Substring(0, commaLine).Trim();
                        lineLbl = maybeLine;
                        return;
                    }
                }
                paraOrCell = tail;
                int estLine = Math.Max(1, ((item.Issue?.StartIndex ?? 0) / 62) + 1);
                lineLbl = UiLocalizer.Pick($"Line {estLine}", $"လိုင်း {estLine}");
            }
            else
            {
                paraOrCell = loc;
                int estLine = Math.Max(1, ((item.Issue?.StartIndex ?? 0) / 62) + 1);
                lineLbl = UiLocalizer.Pick($"Line {estLine}", $"လိုင်း {estLine}");
            }
        }

        private static string BuildMarkedContextSnippet(string cleanParagraph, ProofingIssue iss)
        {
            if (string.IsNullOrWhiteSpace(cleanParagraph))
                return string.Empty;

            string orig = iss.OriginalText ?? string.Empty;
            if (string.IsNullOrEmpty(orig))
            {
                return cleanParagraph.Length > 140
                    ? MyanmarSyllableBreaker.TruncateAtSyllableBoundary(cleanParagraph, 140, "…")
                    : cleanParagraph;
            }

            int hitPos = -1;
            if (iss.StartIndex >= 0 &&
                iss.StartIndex + orig.Length <= cleanParagraph.Length &&
                string.Equals(cleanParagraph.Substring(iss.StartIndex, orig.Length), orig, StringComparison.Ordinal))
            {
                hitPos = iss.StartIndex;
            }
            else
            {
                int searchFrom = Math.Max(0, Math.Min(cleanParagraph.Length - 1, iss.StartIndex - 16));
                hitPos = cleanParagraph.IndexOf(orig, searchFrom, StringComparison.Ordinal);
                if (hitPos < 0)
                    hitPos = cleanParagraph.IndexOf(orig, StringComparison.Ordinal);
            }

            if (hitPos < 0)
            {
                return cleanParagraph.Length > 140
                    ? MyanmarSyllableBreaker.TruncateAtSyllableBoundary(cleanParagraph, 140, "…")
                    : cleanParagraph;
            }

            int winStart = Math.Max(0, hitPos - 52);
            int winEnd = Math.Min(cleanParagraph.Length, hitPos + orig.Length + 62);

            // Snap window start/end to nearest space so words aren't cut mid-syllable
            if (winStart > 0)
            {
                int sp = cleanParagraph.IndexOf(' ', winStart);
                if (sp >= 0 && sp < hitPos - 10)
                    winStart = sp + 1;
            }
            if (winEnd < cleanParagraph.Length)
            {
                int spEnd = cleanParagraph.LastIndexOf(' ', winEnd - 1);
                if (spEnd > hitPos + orig.Length + 10)
                    winEnd = spEnd;
            }

            string prefix = (winStart > 0 ? "…" : string.Empty) + cleanParagraph.Substring(winStart, hitPos - winStart);
            string suffix = cleanParagraph.Substring(hitPos + orig.Length, winEnd - (hitPos + orig.Length)) +
                            (winEnd < cleanParagraph.Length ? "…" : string.Empty);
            return $"{prefix}〖{orig}〗{suffix}";
        }

        internal static string BuildSafeReportFileStem(string documentTitle)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safeTitle = new StringBuilder((documentTitle ?? string.Empty).Length);
            foreach (char ch in documentTitle ?? string.Empty)
            {
                if (Array.IndexOf(invalid, ch) < 0 && ch != ' ')
                    safeTitle.Append(ch);
                else if (ch == ' ')
                    safeTitle.Append('_');
            }
            string stem = safeTitle.Length > 0 ? safeTitle.ToString() : "Document";
            return stem.Length > 40 ? stem.Substring(0, 40) : stem;
        }

        public static string BuildFriendlyCsvText(ProofingAuditReportModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            static string Csv(string? val)
            {
                string s = (val ?? string.Empty).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
                if (s.IndexOf('"') >= 0)
                    s = s.Replace("\"", "\"\"");
                return $"\"{s}\"";
            }

            var sb = new StringBuilder(16384);
            // Friendly Summary Header Rows
            sb.AppendLine(string.Join(",",
                Csv(UiLocalizer.Pick("Myanmar Proofing Audit Report", "မြန်မာစာ စာလုံးပေါင်းနှင့် သဒ္ဒါ စစ်ဆေးချက် အစီရင်ခံစာ")),
                Csv($"{model.DocumentTitle} ({model.HostAppLabel})"),
                Csv(model.OfficialDateHeader),
                Csv($"Score: {model.OverallScore}% ({model.GradeCode}) • Clean: {model.CleanAccuracyPercent:0.##}%"),
                Csv($"Confirmed Wrong: {model.ConfirmedWrongCount}"),
                Csv($"Review Needed: {model.SuspiciousReviewCount}"),
                Csv($"Fixed in Session: {model.SessionFixedCount}")));
            sb.AppendLine();

            if (model.SectionBreakdowns.Count > 0)
            {
                sb.AppendLine(string.Join(",",
                    Csv(UiLocalizer.Pick("Sheet / Slide / Page Breakdown", "Sheet / ဆလိုက် / စာမျက်နှာအလိုက် အနှစ်ချုပ်")),
                    Csv(UiLocalizer.Pick("Confirmed Wrong", "သေချာအမှား")),
                    Csv(UiLocalizer.Pick("Review Needed", "စိစစ်ရန်")),
                    Csv(UiLocalizer.Pick("Total Findings", "စုစုပေါင်း")),
                    Csv(UiLocalizer.Pick("Status", "အခြေအနေ"))));
                for (int s = 0; s < model.SectionBreakdowns.Count; s++)
                {
                    var sec = model.SectionBreakdowns[s];
                    string st = sec.TotalIssues == 0
                        ? UiLocalizer.Pick("✓ Clean", "✓ အမှားကင်း")
                        : UiLocalizer.Pick("Needs Review/Fix", "စိစစ်ပြင်ဆင်ရန်");
                    sb.AppendLine(string.Join(",",
                        Csv(sec.SectionName),
                        sec.ConfirmedWrongCount.ToString(CultureInfo.InvariantCulture),
                        sec.SuspiciousReviewCount.ToString(CultureInfo.InvariantCulture),
                        sec.TotalIssues.ToString(CultureInfo.InvariantCulture),
                        Csv(st)));
                }
                sb.AppendLine();
            }

            // Main Unified Document-Order Table
            sb.AppendLine(string.Join(",",
                Csv("#"),
                Csv(UiLocalizer.Pick("Page / Sheet", "စာမျက်နှာ / Sheet")),
                Csv(UiLocalizer.Pick("Paragraph / Section", "စာပိုဒ် / ဇယား / အကွက်")),
                Csv(UiLocalizer.Pick("Line", "လိုင်းနံပါတ်")),
                Csv(UiLocalizer.Pick("Status", "အခြေအနေ")),
                Csv(UiLocalizer.Pick("Category", "အမျိုးအစား")),
                Csv(UiLocalizer.Pick("Found Word", "တွေ့ရှိသောစာသား")),
                Csv(UiLocalizer.Pick("How It Should Be", "အမှန်ရေးရမည့်စာသား")),
                Csv(UiLocalizer.Pick("Other Options", "အခြားရွေးချယ်စရာ")),
                Csv(UiLocalizer.Pick("Sentence Context (〖Word〗)", "ဝါကျအတွင်း တည်နေရာ (〖စာလုံး〗)")),
                Csv(UiLocalizer.Pick("Simple Explanation", "ရှင်းလင်းချက်"))));

            foreach (var r in model.AllItemsInDocumentOrder)
            {
                sb.AppendLine(string.Join(",",
                    r.DocumentOrderIndex.ToString(CultureInfo.InvariantCulture),
                    Csv(r.PageOrSheetLabel),
                    Csv(r.ParagraphOrCellLabel),
                    Csv(r.LineLabel),
                    Csv(r.StatusBadge),
                    Csv(r.CategoryLabel),
                    Csv(r.VisualOriginal),
                    Csv(r.RecommendedFix),
                    Csv(r.AlternativeFixes),
                    Csv(!string.IsNullOrEmpty(r.MarkedContextSnippet) ? r.MarkedContextSnippet : r.ContextSnippet),
                    Csv(r.Explanation)));
            }

            if (model.SessionFixedRecords.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Join(",",
                    Csv(UiLocalizer.Pick("Already Fixed in This Session", "ယခုအကြိမ် ပြင်ဆင်ပြီးစီးခဲ့သော မှတ်တမ်း")),
                    Csv(UiLocalizer.Pick("Location", "နေရာ")),
                    Csv(UiLocalizer.Pick("Original", "မူလစာသား")),
                    Csv(UiLocalizer.Pick("Applied Fix", "ပြင်ဆင်ပြီးစာသား")),
                    Csv(UiLocalizer.Pick("Time", "အချိန်"))));
                for (int i = 0; i < model.SessionFixedRecords.Count; i++)
                {
                    var f = model.SessionFixedRecords[i];
                    sb.AppendLine(string.Join(",",
                        (i + 1).ToString(CultureInfo.InvariantCulture),
                        Csv(f.LocationLabel),
                        Csv(f.VisualOriginalText),
                        Csv(f.AppliedReplacement),
                        Csv(f.AppliedAtLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture))));
                }
            }

            return sb.ToString();
        }

        public static string SaveFriendlyCsvToDisk(
            ProofingAuditReportModel model,
            string? outputDirectoryOverride = null)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            string baseDir = !string.IsNullOrWhiteSpace(outputDirectoryOverride)
                ? outputDirectoryOverride!
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyanmarSpellChecker",
                    "Reports");
            Directory.CreateDirectory(baseDir);

            string stem = BuildSafeReportFileStem(model.DocumentTitle);
            string fileName = $"ProofingReport_{stem}_{model.GeneratedAtLocal:yyyyMMdd_HHmmss}.csv";
            string fullPath = Path.Combine(baseDir, fileName);
            string csv = BuildFriendlyCsvText(model);
            // UTF-8 BOM (EF BB BF) is mandatory so Microsoft Excel on Windows opens Myanmar Unicode .csv without garbling
            File.WriteAllText(fullPath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return fullPath;
        }

        private static string GetCategoryLabel(ProofingIssue iss, bool isConfirmedWrong)
        {
            return iss.IssueType switch
            {
                ProofingIssueType.LookalikeCharacterError => UiLocalizer.Pick("Unicode / Lookalike", "ယူနီကုဒ် / အက္ခရာတူ"),
                ProofingIssueType.ZawgyiEncodingError => UiLocalizer.Pick("Zawgyi Encoding", "ဇော်ဂျီဖောင့်ရောနှောမှု"),
                ProofingIssueType.NumeralClassifierError => UiLocalizer.Pick("Numeral (တ/တစ်)", "ကိန်းဂဏန်း (တ/တစ်)"),
                ProofingIssueType.OfficialStyleGrammarError => UiLocalizer.Pick("Official Grammar/Style", "ရုံးသုံးသဒ္ဒါ/ဟန်ပန်"),
                ProofingIssueType.PunctuationError => UiLocalizer.Pick("Punctuation (၊ ။)", "ပုဒ်ဖြတ်ပုဒ်ရပ် (၊ ။)"),
                ProofingIssueType.ContextualMeaningWarning => UiLocalizer.Pick("Context Meaning", "အနက်သံသယ"),
                _ => isConfirmedWrong
                    ? UiLocalizer.Pick("Standard Spelling", "စံသတ်ပုံအမှား")
                    : UiLocalizer.Pick("Name / Unrecognized Guess", "အမည် / ခန့်မှန်းစကားလုံး")
            };
        }

        public static string BuildClipboardSummaryText(ProofingAuditReportModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var sb = new StringBuilder(2048);
            sb.AppendLine("================================================================");
            sb.AppendLine(UiLocalizer.Pick(
                "MYANMAR SPELL & GRAMMAR AUDIT REPORT (မြန်မာစာ စစ်ဆေးချက် အစီရင်ခံစာ)",
                "မြန်မာစာ စာလုံးပေါင်းနှင့် သဒ္ဒါ စစ်ဆေးချက် အစီရင်ခံစာ (Audit Report)"));
            sb.AppendLine("================================================================");
            sb.AppendLine($"{UiLocalizer.Pick("Document / File", "စာတမ်းအမည်")} : {model.DocumentTitle} ({model.HostAppLabel})");
            sb.AppendLine($"{UiLocalizer.Pick("Checked At", "စစ်ဆေးချိန်")}      : {model.OfficialDateHeader}");
            sb.AppendLine($"{UiLocalizer.Pick("Proofing Mode", "စစ်ဆေးမှုစနစ်")}   : {model.StyleModeLabel}");
            sb.AppendLine($"{UiLocalizer.Pick("Quality Score", "အရည်အသွေးရမှတ်")}   : {model.OverallScore}% ({model.LocalizedGradeLabel})");
            sb.AppendLine("----------------------------------------------------------------");
            sb.AppendLine(UiLocalizer.Pick(
                "1. 4-PART WORD HEALTH SCORECARD (စကားလုံး အရည်အသွေး အနှစ်ချုပ်)",
                "၁။ စကားလုံး အရည်အသွေး ၄-ပိုင်းခွဲ အနှစ်ချုပ် (4-Part Word Health Scorecard)"));
            sb.AppendLine($"   • {UiLocalizer.Pick("Total Words / Syllables", "စုစုပေါင်း စကားလုံး / ဝဏ္ဏ")} : {model.TotalWords:N0} words ({model.MyanmarSyllables:N0} syllables)");
            sb.AppendLine($"   • {UiLocalizer.Pick("Clean / Valid Rate", "အမှားကင်းစင်မှုနှုန်း")}       : {model.CleanAccuracyPercent:0.##}% (~{model.EstimatedCleanWords:N0} words)");
            sb.AppendLine($"   • {UiLocalizer.Pick("Confirmed Wrong (Sure)", "သေချာသော အမှားစစ်စစ်")}     : {model.ConfirmedWrongCount:N0} ({model.ConfirmedWrongRatePercent:0.##}%)  [Spelling: {model.ConfirmedSpellingCount}, Unicode/Lookalike: {model.ConfirmedEncodingAndLookalikeCount}, Grammar: {model.ConfirmedGrammarAndStyleCount}]");
            sb.AppendLine($"   • {UiLocalizer.Pick("Suspicious / Review", "သံသယ / လူစိစစ်ရန်")}        : {model.SuspiciousReviewCount:N0} ({model.SuspiciousRatePercent:0.##}%)  [Names/Guesses: {model.SuspiciousNameOrOovCount}, Meaning Context: {model.SuspiciousContextMeaningCount}, Consistency: {model.ConsistencyWarningCount}]");
            sb.AppendLine($"   • {UiLocalizer.Pick("Already Fixed (Session)", "ယခုပြင်ဆင်ပြီး အရေအတွက်")}   : {model.SessionFixedCount:N0}");
            sb.AppendLine();

            if (model.TopRepeatedGroups.Count > 0)
            {
                sb.AppendLine(UiLocalizer.Pick(
                    "2. HOW IT SHOULD BE — TOP FINDINGS (မည်သို့ပြင်ဆင်သင့်သည်)",
                    "၂။ အဓိက ပြင်ဆင်ရန်နှင့် သံသယရှိ စာလုံးများ (Found ➔ How It Should Be)"));
                int take = Math.Min(25, model.TopRepeatedGroups.Count);
                for (int i = 0; i < take; i++)
                {
                    var g = model.TopRepeatedGroups[i];
                    string cntTag = g.Count > 1 ? $" [×{g.Count}]" : string.Empty;
                    sb.AppendLine($"   {i + 1}. [{g.StatusBadge}] \"{g.VisualOriginal}\"{cntTag}  ➔  \"{g.RecommendedFix}\"  ({g.SampleLocations})");
                }
                sb.AppendLine();
            }

            if (model.SessionFixedRecords.Count > 0)
            {
                sb.AppendLine(UiLocalizer.Pick(
                    $"3. ALREADY FIXED IN THIS SESSION ({model.SessionFixedCount})",
                    $"၃။ ယခုအကြိမ် ပြင်ဆင်ပြီးစီးခဲ့သော စာလုံးများ ({model.SessionFixedCount} ခု)"));
                int takeFix = Math.Min(15, model.SessionFixedRecords.Count);
                for (int i = 0; i < takeFix; i++)
                {
                    var f = model.SessionFixedRecords[i];
                    sb.AppendLine($"   ✓ [{f.LocationLabel}] \"{f.VisualOriginalText}\"  ➔  \"{f.AppliedReplacement}\"");
                }
            }

            return sb.ToString();
        }

        public static string SaveStandaloneInteractiveHtmlToDisk(
            ProofingAuditReportModel model,
            string? outputDirectoryOverride = null)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            string baseDir = !string.IsNullOrWhiteSpace(outputDirectoryOverride)
                ? outputDirectoryOverride!
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyanmarSpellChecker",
                    "Reports");
            Directory.CreateDirectory(baseDir);

            var invalid = Path.GetInvalidFileNameChars();
            var safeTitle = new StringBuilder(model.DocumentTitle.Length);
            foreach (char ch in model.DocumentTitle)
            {
                if (Array.IndexOf(invalid, ch) < 0 && ch != ' ')
                    safeTitle.Append(ch);
                else if (ch == ' ')
                    safeTitle.Append('_');
            }
            string stem = safeTitle.Length > 0 ? safeTitle.ToString() : "Document";
            if (stem.Length > 40)
                stem = stem.Substring(0, 40);

            string fileName = $"ProofingReport_{stem}_{model.GeneratedAtLocal:yyyyMMdd_HHmmss}.html";
            string fullPath = Path.Combine(baseDir, fileName);
            string html = BuildStandaloneInteractiveHtml(model);
            File.WriteAllText(fullPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return fullPath;
        }

        public static string BuildStandaloneInteractiveHtml(ProofingAuditReportModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

            double cleanPct = Math.Max(0.0, Math.Min(100.0, model.CleanAccuracyPercent));
            double wrongPct = Math.Max(0.0, Math.Min(100.0 - cleanPct, model.ConfirmedWrongRatePercent));
            double suspPct = Math.Max(0.0, Math.Min(100.0 - cleanPct - wrongPct, model.SuspiciousRatePercent));

            string cleanStr = cleanPct.ToString("0.##", CultureInfo.InvariantCulture);
            string wrongStr = wrongPct.ToString("0.##", CultureInfo.InvariantCulture);
            string suspStr = suspPct.ToString("0.##", CultureInfo.InvariantCulture);

            // SVG Donut stroke-dasharray (circumference = 2 * pi * 42 ≈ 263.89)
            const double circ = 263.89;
            double cleanDash = (cleanPct / 100.0) * circ;
            double wrongDash = (wrongPct / 100.0) * circ;
            double suspDash = (suspPct / 100.0) * circ;
            double wrongOffset = -cleanDash;
            double suspOffset = -(cleanDash + wrongDash);

            var sb = new StringBuilder(32768);
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"my\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\" />");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.AppendLine($"<title>{H(model.DocumentTitle)} — Myanmar Proofing Audit Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(@"
  :root {
    --navy: #102a48; --blue: #125cb2; --green: #106c38; --green-bg: #e4f7eb;
    --red: #be1e2d; --red-bg: #fdecec; --amber: #855a00; --amber-bg: #fef9c3;
    --slate: #475569; --border: #d5e0ec; --bg: #f4f7fb;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0; padding: 24px; background: var(--bg); color: #1a2636;
    font-family: 'Pyidaungsu', 'Myanmar Text', 'Segoe UI', system-ui, sans-serif;
    line-height: 1.6;
  }
  .container { max-width: 1220px; margin: 0 auto; background: #fff; border: 1px solid var(--border); border-radius: 10px; padding: 28px; box-shadow: 0 4px 18px rgba(16,42,72,0.06); }
  .top-bar { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start; gap: 16px; border-bottom: 2px solid var(--border); padding-bottom: 18px; margin-bottom: 22px; }
  h1 { margin: 0 0 6px 0; font-size: 21px; color: var(--navy); }
  .meta { font-size: 13.5px; color: var(--slate); }
  .actions { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
  .btn {
    border: 1px solid var(--blue); background: var(--blue); color: #fff; font-family: inherit;
    font-size: 13.5px; font-weight: 600; padding: 8px 15px; border-radius: 6px; cursor: pointer;
  }
  .btn-outline { background: #fff; color: var(--navy); border-color: var(--border); }
  .btn-outline.active { background: var(--navy); color: #fff; border-color: var(--navy); }
  .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(225px, 1fr)); gap: 14px; margin-bottom: 22px; }
  .kpi { border: 1px solid var(--border); border-radius: 8px; padding: 14px 16px; background: #fafcff; border-left: 5px solid var(--blue); }
  .kpi.clean { border-left-color: var(--green); }
  .kpi.wrong { border-left-color: var(--red); background: #fffafa; }
  .kpi.susp { border-left-color: #eab308; background: #fefce8; }
  .kpi.fixed { border-left-color: #0e7490; }
  .kpi-label { font-size: 12.5px; font-weight: 700; color: var(--slate); text-transform: uppercase; letter-spacing: 0.02em; }
  .kpi-val { font-size: 26px; font-weight: 800; margin: 4px 0; color: var(--navy); }
  .kpi.wrong .kpi-val { color: var(--red); }
  .kpi.susp .kpi-val { color: var(--amber); }
  .kpi.clean .kpi-val { color: var(--green); }
  .kpi-sub { font-size: 12.5px; color: var(--slate); }
  .visual-row { display: flex; flex-wrap: wrap; gap: 22px; align-items: center; border: 1px solid var(--border); border-radius: 8px; padding: 16px 20px; margin-bottom: 22px; background: #fbfdff; }
  .bar-wrap { flex: 1; min-width: 260px; }
  .stacked-bar { display: flex; height: 18px; border-radius: 9px; overflow: hidden; background: #e2e8f0; margin: 8px 0 10px 0; }
  .seg-clean { background: #16a34a; }
  .seg-wrong { background: #dc2626; }
  .seg-susp { background: #eab308; }
  .legend { display: flex; gap: 18px; flex-wrap: wrap; font-size: 13px; color: var(--slate); }
  .dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: 5px; }
  .filter-bar { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 10px; margin: 18px 0 14px 0; }
  .search-input { padding: 7px 12px; border: 1px solid var(--border); border-radius: 6px; font-family: inherit; font-size: 13.5px; min-width: 250px; }
  h2 { font-size: 16.5px; color: var(--navy); margin: 22px 0 10px 0; padding-bottom: 6px; border-bottom: 1px solid var(--border); }
  table { width: 100%; border-collapse: collapse; margin-bottom: 20px; font-size: 13.5px; }
  th { background: #eef3fa; color: var(--navy); text-align: left; padding: 9px 10px; border: 1px solid var(--border); font-weight: 700; }
  td { padding: 8px 10px; border: 1px solid var(--border); vertical-align: top; }
  tr:nth-child(even) td { background: #fafcfe; }
  .badge { display: inline-block; padding: 2px 8px; border-radius: 99px; font-size: 12px; font-weight: 700; white-space: nowrap; }
  .badge-wrong { background: var(--red-bg); color: var(--red); }
  .badge-susp { background: var(--amber-bg); color: var(--amber); }
  .badge-clean { background: var(--green-bg); color: var(--green); }
  .orig-word { font-weight: 700; color: #991b1b; }
  .fix-word { font-weight: 700; color: var(--green); }
  .ctx-text { color: var(--slate); font-size: 12.5px; margin-top: 3px; }
  .footer-note { margin-top: 26px; padding-top: 12px; border-top: 1px solid var(--border); font-size: 12px; color: var(--slate); }
  @media print {
    body { background: #fff; padding: 0; }
    .container { border: none; box-shadow: none; padding: 0; max-width: 100%; }
    .no-print { display: none !important; }
    section { display: block !important; }
    tr { page-break-inside: avoid; }
  }
");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"container\">");

            // Header
            sb.AppendLine("<div class=\"top-bar\">");
            sb.AppendLine("<div>");
            sb.AppendLine($"<h1>{H(UiLocalizer.Pick("Myanmar Proofing & Orthography Audit Report", "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံနှင့် သဒ္ဒါ စစ်ဆေးချက် အစီရင်ခံစာ"))}</h1>");
            sb.AppendLine($"<div class=\"meta\"><strong>{H(model.DocumentTitle)}</strong> ({H(model.HostAppLabel)}) &nbsp;•&nbsp; {H(model.StyleModeLabel)}<br/>{H(model.OfficialDateHeader)}</div>");
            sb.AppendLine("</div>");
            sb.AppendLine("<div class=\"actions no-print\">");
            sb.AppendLine($"<button class=\"btn\" onclick=\"window.print()\">⎙ {H(UiLocalizer.Pick("Print / Save as PDF", "ပုံနှိပ်ရန် / PDF သိမ်းရန်"))}</button>");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");

            // 4-Part KPI Cards
            sb.AppendLine("<div class=\"kpi-grid\">");
            sb.AppendLine($"<div class=\"kpi clean\"><div class=\"kpi-label\">{H(UiLocalizer.Pick("Clean Accuracy & Grade • အရည်အသွေး", "အမှားကင်းစင်မှုနှင့် အဆင့် • Accuracy"))}</div><div class=\"kpi-val\">{cleanStr}% ({H(model.GradeCode)})</div><div class=\"kpi-sub\">{model.TotalWords:N0} words ({model.MyanmarSyllables:N0} syllables) • {H(model.LocalizedGradeLabel)}</div></div>");
            sb.AppendLine($"<div class=\"kpi wrong\"><div class=\"kpi-label\">{H(UiLocalizer.Pick("Confirmed Wrong • သေချာအမှား", "သေချာသော အမှားစစ်စစ် • Confirmed Wrong"))}</div><div class=\"kpi-val\">{model.ConfirmedWrongCount:N0} <span style=\"font-size:15px;font-weight:600;\">({wrongStr}%)</span></div><div class=\"kpi-sub\">Spelling: {model.ConfirmedSpellingCount} • Unicode/Lookalike: {model.ConfirmedEncodingAndLookalikeCount} • Grammar: {model.ConfirmedGrammarAndStyleCount}</div></div>");
            sb.AppendLine($"<div class=\"kpi susp\"><div class=\"kpi-label\">{H(UiLocalizer.Pick("Suspicious / Review • သံသယစိစစ်ရန်", "သံသယ / လူစိစစ်ရန် • Suspicious"))}</div><div class=\"kpi-val\">{model.SuspiciousReviewCount:N0} <span style=\"font-size:15px;font-weight:600;\">({suspStr}%)</span></div><div class=\"kpi-sub\">Names/Guesses: {model.SuspiciousNameOrOovCount} • Meaning: {model.SuspiciousContextMeaningCount} • Consistency: {model.ConsistencyWarningCount}</div></div>");
            sb.AppendLine($"<div class=\"kpi fixed\"><div class=\"kpi-label\">{H(UiLocalizer.Pick("Fixed in Session • ပြင်ပြီးစာလုံး", "ယခုပြင်ဆင်ပြီး • Fixed in Session"))}</div><div class=\"kpi-val\">{model.SessionFixedCount:N0}</div><div class=\"kpi-sub\">Unicode Purity: {model.UnicodePurityPercent:0.#}% • Formal Style: {model.FormalRegisterPercent:0.#}%</div></div>");
            sb.AppendLine("</div>");

            // Visual Donut + Stacked Bar
            sb.AppendLine("<div class=\"visual-row\">");
            sb.AppendLine($"<svg width=\"108\" height=\"108\" viewBox=\"0 0 108 108\" aria-label=\"Accuracy Chart\">");
            sb.AppendLine("<circle cx=\"54\" cy=\"54\" r=\"42\" fill=\"none\" stroke=\"#e2e8f0\" stroke-width=\"13\" />");
            sb.AppendLine($"<circle cx=\"54\" cy=\"54\" r=\"42\" fill=\"none\" stroke=\"#16a34a\" stroke-width=\"13\" stroke-dasharray=\"{cleanDash.ToString("0.##", CultureInfo.InvariantCulture)} {circ}\" transform=\"rotate(-90 54 54)\" />");
            if (wrongDash > 0.05)
                sb.AppendLine($"<circle cx=\"54\" cy=\"54\" r=\"42\" fill=\"none\" stroke=\"#dc2626\" stroke-width=\"13\" stroke-dasharray=\"{wrongDash.ToString("0.##", CultureInfo.InvariantCulture)} {circ}\" stroke-dashoffset=\"{wrongOffset.ToString("0.##", CultureInfo.InvariantCulture)}\" transform=\"rotate(-90 54 54)\" />");
            if (suspDash > 0.05)
                sb.AppendLine($"<circle cx=\"54\" cy=\"54\" r=\"42\" fill=\"none\" stroke=\"#f59e0b\" stroke-width=\"13\" stroke-dasharray=\"{suspDash.ToString("0.##", CultureInfo.InvariantCulture)} {circ}\" stroke-dashoffset=\"{suspOffset.ToString("0.##", CultureInfo.InvariantCulture)}\" transform=\"rotate(-90 54 54)\" />");
            sb.AppendLine($"<text x=\"54\" y=\"58\" text-anchor=\"middle\" font-size=\"16\" font-weight=\"800\" fill=\"#102a48\">{model.OverallScore}%</text>");
            sb.AppendLine("</svg>");

            sb.AppendLine("<div class=\"bar-wrap\">");
            sb.AppendLine($"<div style=\"font-weight:700;color:#102a48;\">{H(UiLocalizer.Pick("Document Word Health Breakdown (စာလုံးအချိုးအစား)", "စာတမ်းတစ်ခုလုံးရှိ စာလုံးအရည်အသွေး အချိုးအစား (Word Health Breakdown)"))}</div>");
            sb.AppendLine("<div class=\"stacked-bar\">");
            sb.AppendLine($"<div class=\"seg-clean\" style=\"width:{cleanStr}%\"></div>");
            sb.AppendLine($"<div class=\"seg-wrong\" style=\"width:{wrongStr}%\"></div>");
            sb.AppendLine($"<div class=\"seg-susp\" style=\"width:{suspStr}%\"></div>");
            sb.AppendLine("</div>");
            sb.AppendLine("<div class=\"legend\">");
            sb.AppendLine($"<span><i class=\"dot\" style=\"background:#16a34a\"></i>Clean / Valid: <strong>{cleanStr}%</strong> (~{model.EstimatedCleanWords:N0})</span>");
            sb.AppendLine($"<span><i class=\"dot\" style=\"background:#dc2626\"></i>Confirmed Wrong: <strong>{model.ConfirmedWrongCount:N0}</strong> ({wrongStr}%)</span>");
            sb.AppendLine($"<span><i class=\"dot\" style=\"background:#f59e0b\"></i>Suspicious / Review: <strong>{model.SuspiciousReviewCount:N0}</strong> ({suspStr}%)</span>");
            sb.AppendLine("</div>");
            if (model.ConsistencySummaries.Count > 0)
            {
                sb.AppendLine($"<div class=\"ctx-text\" style=\"margin-top:8px;\"><strong>Consistency Notes:</strong> {H(string.Join(" | ", model.ConsistencySummaries))}</div>");
            }
            sb.AppendLine("</div></div>");

            // Interactive Filter Tabs + Search (hidden in print)
            sb.AppendLine("<div class=\"filter-bar no-print\">");
            sb.AppendLine("<div class=\"actions\">");
            sb.AppendLine($"<button class=\"btn btn-outline active\" onclick=\"showSec('all', this)\">All Sections</button>");
            sb.AppendLine($"<button class=\"btn btn-outline\" onclick=\"showSec('sec-friendly-all', this)\">Friendly Checklist ({model.AllItemsInDocumentOrder.Count})</button>");
            sb.AppendLine($"<button class=\"btn btn-outline\" onclick=\"showSec('sec-wrong', this)\">Confirmed Wrong ({model.ConfirmedWrongCount})</button>");
            sb.AppendLine($"<button class=\"btn btn-outline\" onclick=\"showSec('sec-susp', this)\">Review Needed ({model.SuspiciousReviewCount})</button>");
            sb.AppendLine($"<button class=\"btn btn-outline\" onclick=\"showSec('sec-repeated', this)\">Repeated ({model.TopRepeatedGroups.Count})</button>");
            sb.AppendLine($"<button class=\"btn btn-outline\" onclick=\"showSec('sec-fixed', this)\">Fixed ({model.SessionFixedCount})</button>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<input id=\"qBox\" class=\"search-input\" type=\"search\" placeholder=\"{H(UiLocalizer.Pick("Search page, paragraph, line, or word...", "စာမျက်နှာ၊ စာပိုဒ်၊ လိုင်း သို့မဟုတ် စာလုံး ရှာရန်..."))}\" oninput=\"filterRows(this.value)\" />");
            sb.AppendLine("</div>");

            static string RenderMarkedHtml(string? marked, bool isWrong)
            {
                string raw = marked ?? string.Empty;
                int open = raw.IndexOf('〖');
                int close = open >= 0 ? raw.IndexOf('〗', open + 1) : -1;
                if (open < 0 || close <= open)
                    return WebUtility.HtmlEncode(raw);

                string bg = isWrong ? "#fee2e2" : "#fef08a";
                string fg = isWrong ? "#991b1b" : "#854d0e";
                return WebUtility.HtmlEncode(raw.Substring(0, open)) +
                       $"<mark style=\"background:{bg};color:{fg};font-weight:700;padding:1px 5px;border-radius:4px;\">" +
                       WebUtility.HtmlEncode(raw.Substring(open + 1, close - open - 1)) +
                       "</mark>" +
                       WebUtility.HtmlEncode(raw.Substring(close + 1));
            }

            // Friendly Section 0: Unified Document-Order Checklist (Page | Paragraph | Line | Status | Found -> How It Should Be | Sentence)
            sb.AppendLine("<section id=\"sec-friendly-all\">");
            sb.AppendLine($"<h2>1. {H(UiLocalizer.Pick($"Friendly Reading-Order Checklist ({model.AllItemsInDocumentOrder.Count}) • စာမျက်နှာ၊ စာပိုဒ်နှင့် လိုင်းအလိုက် စစ်ဆေးချက်ဇယား", $"စာမျက်နှာ၊ စာပိုဒ်နှင့် လိုင်းအလိုက် အစဉ်လိုက် ဖတ်ရှုစစ်ဆေးရန် ဇယား ({model.AllItemsInDocumentOrder.Count})"))}</h2>");
            if (model.AllItemsInDocumentOrder.Count == 0)
            {
                sb.AppendLine($"<p class=\"badge badge-clean\">✓ {H(UiLocalizer.Pick("100% clean — zero unresolved issues!", "ပြင်ဆင်ရန် အမှားအယွင်း လုံးဝမကျန်ရှိပါ။"))}</p>");
            }
            else
            {
                sb.AppendLine("<table class=\"data-tbl\"><thead><tr><th>#</th><th>Page / Sheet</th><th>Paragraph / Section</th><th>Line</th><th>Status</th><th>Found • တွေ့ရှိစာသား</th><th>How It Should Be • အမှန်ရေးရန်</th><th>Sentence &amp; Explanation • ဝါကျနှင့် ရှင်းလင်းချက်</th></tr></thead><tbody>");
                foreach (var r in model.AllItemsInDocumentOrder)
                {
                    string bCls = r.IsConfirmedWrong ? "badge-wrong" : "badge-susp";
                    string wStyle = r.IsConfirmedWrong ? "class=\"orig-word\"" : "style=\"font-weight:700;color:#855a00;\"";
                    string ctxHtml = RenderMarkedHtml(!string.IsNullOrEmpty(r.MarkedContextSnippet) ? r.MarkedContextSnippet : r.ContextSnippet, r.IsConfirmedWrong);
                    sb.AppendLine($"<tr><td>{r.DocumentOrderIndex}</td><td><strong>{H(r.PageOrSheetLabel)}</strong></td><td><strong>{H(r.ParagraphOrCellLabel)}</strong></td><td><strong>{H(r.LineLabel)}</strong></td><td><span class=\"badge {bCls}\">{H(r.StatusBadge)}</span></td><td {wStyle}>{H(r.VisualOriginal)}</td><td class=\"fix-word\">{H(r.HowItShouldBeDisplay)}</td><td><div style=\"margin-bottom:3px;\">{ctxHtml}</div><div class=\"ctx-text\">{H(r.Explanation)}</div></td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
            sb.AppendLine("</section>");

            // Section A: Top Repeated Mistakes
            sb.AppendLine("<section id=\"sec-repeated\">");
            sb.AppendLine($"<h2>A. {H(UiLocalizer.Pick("Top Repeated Findings Summary (ထပ်ခါတလဲလဲ တွေ့ရှိချက်များ)", "ထပ်ခါတလဲလဲ အများဆုံးတွေ့ရှိရသော စာလုံးများ အနှစ်ချုပ် (Top Repeated Findings)"))}</h2>");
            if (model.TopRepeatedGroups.Count == 0)
            {
                sb.AppendLine($"<p class=\"badge badge-clean\">✓ {H(UiLocalizer.Pick("Zero unresolved issues found!", "ပြင်ဆင်ရန် အမှားအယွင်း မကျန်ရှိပါ။"))}</p>");
            }
            else
            {
                sb.AppendLine("<table class=\"data-tbl\"><thead><tr><th>#</th><th>Status • အမျိုးအစား</th><th>Found in Document • တွေ့ရှိစာသား</th><th>Count • အကြိမ်ရေ</th><th>How It Should Be • အမှန်ရေးရန်</th><th>Locations • နေရာများ</th></tr></thead><tbody>");
                for (int i = 0; i < model.TopRepeatedGroups.Count; i++)
                {
                    var g = model.TopRepeatedGroups[i];
                    string bCls = g.IsConfirmedWrong ? "badge-wrong" : "badge-susp";
                    sb.AppendLine($"<tr><td>{i + 1}</td><td><span class=\"badge {bCls}\">{H(g.StatusBadge)}</span></td><td class=\"orig-word\">{H(g.VisualOriginal)}</td><td><strong>×{g.Count}</strong></td><td class=\"fix-word\">{H(g.RecommendedFix)}</td><td>{H(g.SampleLocations)}</td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
            sb.AppendLine("</section>");

            // Section B1: Confirmed Wrong — How It Should Be
            sb.AppendLine("<section id=\"sec-wrong\">");
            sb.AppendLine($"<h2>B1. {H(UiLocalizer.Pick($"Confirmed Wrong — How It Should Be ({model.ConfirmedWrongCount}) • သေချာသော အမှားများနှင့် ပြင်ဆင်ရန်အမှန်", $"သေချာသော အမှားစစ်စစ်များနှင့် မည်သို့ရေးရမည် ({model.ConfirmedWrongCount}) • Confirmed Wrong"))}</h2>");
            if (model.ConfirmedWrongItems.Count == 0)
            {
                sb.AppendLine($"<p class=\"badge badge-clean\">✓ {H(UiLocalizer.Pick("0 confirmed errors remaining.", "သေချာသော စာလုံးပေါင်း/သဒ္ဒါအမှား လုံးဝမရှိပါ။"))}</p>");
            }
            else
            {
                sb.AppendLine("<table class=\"data-tbl\"><thead><tr><th>#</th><th>Location • နေရာ</th><th>Category</th><th>Wrong Text • မှားနေသောစာသား</th><th>How It Should Be • အမှန်ဖြစ်ရမည့်စာသား</th><th>Context &amp; MLC Rule • ဝါကျနှင့် စည်းကမ်းချက်</th></tr></thead><tbody>");
                foreach (var r in model.ConfirmedWrongItems)
                {
                    sb.AppendLine($"<tr><td>{r.Index}</td><td><strong>{H(r.LocationLabel)}</strong></td><td><span class=\"badge badge-wrong\">{H(r.CategoryLabel)}</span></td><td class=\"orig-word\">{H(r.VisualOriginal)}</td><td class=\"fix-word\">{H(r.HowItShouldBeDisplay)}</td><td><div>{H(r.Explanation)}</div><div class=\"ctx-text\">{H(r.ContextSnippet)}</div></td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
            sb.AppendLine("</section>");

            // Section B2: Suspicious / Review Needed
            sb.AppendLine("<section id=\"sec-susp\">");
            sb.AppendLine($"<h2>B2. {H(UiLocalizer.Pick($"Suspicious / Human Review Needed ({model.SuspiciousReviewCount}) • သံသယရှိ / လူစိစစ်ရန် စာလုံးများ", $"သံသယရှိ / လူကိုယ်တိုင် စိစစ်အတည်ပြုရန် စာလုံးများ ({model.SuspiciousReviewCount}) • Suspicious / Review"))}</h2>");
            if (model.SuspiciousReviewItems.Count == 0)
            {
                sb.AppendLine($"<p class=\"badge badge-clean\">✓ {H(UiLocalizer.Pick("0 suspicious items requiring manual review.", "သံသယရှိ စိစစ်ရန်စာလုံး မရှိပါ။"))}</p>");
            }
            else
            {
                sb.AppendLine("<table class=\"data-tbl\"><thead><tr><th>#</th><th>Location • နေရာ</th><th>Why Suspicious • သံသယအမျိုးအစား</th><th>Suspicious Text • စစ်ဆေးတွေ့ရှိချက်</th><th>Suggested (If Typo) • အမှားဖြစ်ပါက ပြင်ရန်</th><th>Context &amp; Note • ဝါကျနှင့် မှတ်ချက်</th></tr></thead><tbody>");
                foreach (var r in model.SuspiciousReviewItems)
                {
                    sb.AppendLine($"<tr><td>{r.Index}</td><td><strong>{H(r.LocationLabel)}</strong></td><td><span class=\"badge badge-susp\">{H(r.CategoryLabel)}</span></td><td style=\"font-weight:700;color:#92400e;\">{H(r.VisualOriginal)}</td><td class=\"fix-word\">{H(r.HowItShouldBeDisplay)}</td><td><div>{H(r.Explanation)}</div><div class=\"ctx-text\">{H(r.ContextSnippet)}</div></td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
            sb.AppendLine("</section>");

            // Section C: Already Fixed in Session
            sb.AppendLine("<section id=\"sec-fixed\">");
            sb.AppendLine($"<h2>C. {H(UiLocalizer.Pick($"Already Fixed in This Session ({model.SessionFixedCount}) • ယခုပြင်ဆင်ပြီးစီးခဲ့သော စာလုံးများ", $"ယခုအကြိမ် ပြင်ဆင်ပြီးစီးခဲ့သော စာလုံးများ ({model.SessionFixedCount}) • Fixed in Session"))}</h2>");
            if (model.SessionFixedRecords.Count == 0)
            {
                sb.AppendLine($"<p class=\"meta\">{H(UiLocalizer.Pick("No fixes applied yet in this session.", "ယခုအကြိမ်အတွင်း ပြင်ဆင်ထားသောမှတ်တမ်း မရှိသေးပါ။"))}</p>");
            }
            else
            {
                sb.AppendLine("<table class=\"data-tbl\"><thead><tr><th>#</th><th>Location • နေရာ</th><th>Original • မူလအမှား</th><th>Applied Fix • ပြင်ဆင်ပြီးစာသား</th><th>Time</th></tr></thead><tbody>");
                for (int i = 0; i < model.SessionFixedRecords.Count; i++)
                {
                    var f = model.SessionFixedRecords[i];
                    sb.AppendLine($"<tr><td>{i + 1}</td><td>{H(f.LocationLabel)}</td><td class=\"orig-word\">{H(f.VisualOriginalText)}</td><td class=\"fix-word\">✓ {H(f.AppliedReplacement)}</td><td>{f.AppliedAtLocal:HH:mm:ss}</td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
            sb.AppendLine("</section>");

            sb.AppendLine("<div class=\"footer-note\">");
            sb.AppendLine("Reference Standard: Myanmar Language Commission (မြန်မာစာအဖွဲ့) 'မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း' &amp; 'မြန်မာအဘိဓာန်' (100% Offline Verification) • Creator: Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University.");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");

            // Lightweight offline JS for section tabs & search filter
            sb.AppendLine("<script>");
            sb.AppendLine(@"
function showSec(id, btn) {
  var btns = document.querySelectorAll('.filter-bar .btn-outline');
  for (var i = 0; i < btns.length; i++) btns[i].classList.remove('active');
  if (btn) btn.classList.add('active');
  var secs = document.querySelectorAll('section');
  for (var j = 0; j < secs.length; j++) {
    secs[j].style.display = (id === 'all' || secs[j].id === id) ? 'block' : 'none';
  }
}
function filterRows(q) {
  var needle = (q || '').toLowerCase().trim();
  var rows = document.querySelectorAll('table.data-tbl tbody tr');
  for (var i = 0; i < rows.length; i++) {
    var txt = rows[i].textContent.toLowerCase();
    rows[i].style.display = (!needle || txt.indexOf(needle) >= 0) ? '' : 'none';
  }
}
");
            sb.AppendLine("</script>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }
    }
}
