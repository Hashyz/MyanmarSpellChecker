// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.Core.Typography;

namespace MyanmarSpellChecker.Core.Presentation
{
    public enum PresentationScanScope
    {
        ActiveSlide = 0,
        Selection = 1,
        EntirePresentation = 2
    }

    public enum PresentationContainerKind
    {
        SlideShape = 0,
        TableCell = 1,
        GroupItemShape = 2,
        SpeakerNotes = 3,
        SlideMasterShape = 4
    }

    /// <summary>
    /// Decoupled, COM-free snapshot of a single PowerPoint text container (Slide TextBox/Placeholder,
    /// Table Cell, Nested GroupItem Shape, Speaker Notes, or Slide Master/Layout shape).
    /// Extracted on PowerPoint's STA thread so NLP analysis and batch planning run 100% read-only
    /// without mutating slide formatting or polluting PowerPoint's Undo history.
    /// </summary>
    public sealed class PresentationTextTarget
    {
        public string PresentationName { get; set; } = string.Empty;
        public int SlideId { get; set; }
        public int SlideNumber { get; set; }
        public string SlideTitle { get; set; } = string.Empty;
        public PresentationContainerKind ContainerKind { get; set; }
        public int ShapeId { get; set; }
        public string ShapeName { get; set; } = string.Empty;
        public int[] GroupPathIds { get; set; } = Array.Empty<int>();
        public int TableRow { get; set; }
        public int TableColumn { get; set; }
        public string MasterOrLayoutName { get; set; } = string.Empty;
        public string TextSnapshot { get; set; } = string.Empty;
        public string PrimaryFontName { get; set; } = string.Empty;

        public string BuildTargetKey()
        {
            string groupKey = (GroupPathIds != null && GroupPathIds.Length > 0)
                ? string.Join("/", GroupPathIds)
                : string.Empty;
            return string.Concat(
                PresentationName, "|",
                ((int)ContainerKind).ToString(CultureInfo.InvariantCulture), "|",
                SlideId.ToString(CultureInfo.InvariantCulture), "|",
                MasterOrLayoutName, "|",
                groupKey, "|",
                ShapeId.ToString(CultureInfo.InvariantCulture), "|",
                TableRow.ToString(CultureInfo.InvariantCulture), ",",
                TableColumn.ToString(CultureInfo.InvariantCulture));
        }

        public string BuildDisplayLocation()
        {
            bool en = UiLocalizer.IsEnglish;
            string slidePart = ContainerKind == PresentationContainerKind.SlideMasterShape
                ? (string.IsNullOrWhiteSpace(MasterOrLayoutName)
                    ? (en ? "Master" : "မူရင်းပုံစံ")
                    : MasterOrLayoutName)
                : (en
                    ? $"Slide {SlideNumber.ToString(CultureInfo.InvariantCulture)}"
                    : $"ဆလိုက် {SlideNumber.ToString(CultureInfo.InvariantCulture)}");

            return ContainerKind switch
            {
                PresentationContainerKind.TableCell => en
                    ? $"{slidePart} - Table [R{TableRow},C{TableColumn}]"
                    : $"{slidePart} - ဇယား [တန်း {TableRow}, တိုင် {TableColumn}]",
                PresentationContainerKind.SpeakerNotes => en
                    ? $"{slidePart} - Speaker Notes"
                    : $"{slidePart} - ဟောပြောချက်မှတ်စု",
                PresentationContainerKind.GroupItemShape => string.IsNullOrWhiteSpace(ShapeName)
                    ? (en ? $"{slidePart} - Group Shape #{ShapeId}" : $"{slidePart} - အုပ်စုပုံ #{ShapeId}")
                    : (en ? $"{slidePart} - Group/{ShapeName}" : $"{slidePart} - အုပ်စု/{ShapeName}"),
                PresentationContainerKind.SlideMasterShape => string.IsNullOrWhiteSpace(ShapeName)
                    ? $"{slidePart} - #{ShapeId}"
                    : $"{slidePart} - {ShapeName}",
                _ => string.IsNullOrWhiteSpace(ShapeName)
                    ? $"{slidePart} - #{ShapeId}"
                    : $"{slidePart} - {ShapeName}"
            };
        }

        public PresentationTextTarget CloneWithText(string updatedText, string? updatedFontName = null)
        {
            return new PresentationTextTarget
            {
                PresentationName = PresentationName,
                SlideId = SlideId,
                SlideNumber = SlideNumber,
                SlideTitle = SlideTitle,
                ContainerKind = ContainerKind,
                ShapeId = ShapeId,
                ShapeName = ShapeName,
                GroupPathIds = GroupPathIds != null && GroupPathIds.Length > 0 ? (int[])GroupPathIds.Clone() : Array.Empty<int>(),
                TableRow = TableRow,
                TableColumn = TableColumn,
                MasterOrLayoutName = MasterOrLayoutName,
                TextSnapshot = updatedText ?? string.Empty,
                PrimaryFontName = updatedFontName ?? PrimaryFontName
            };
        }
    }

    /// <summary>
    /// Represents a single proofing issue anchored to a specific PowerPoint slide/table/group/notes text container.
    /// </summary>
    public sealed class PresentationSlideIssue
    {
        public PresentationTextTarget Target { get; set; } = new PresentationTextTarget();
        public ProofingIssue Issue { get; set; } = new ProofingIssue();
        public bool IsResolved { get; set; }

        public string PresentationName => Target.PresentationName;
        public int SlideId => Target.SlideId;
        public int SlideNumber => Target.SlideNumber;
        public PresentationContainerKind ContainerKind => Target.ContainerKind;
        public int ShapeId => Target.ShapeId;
        public int TableRow => Target.TableRow;
        public int TableColumn => Target.TableColumn;
        public string SnapshotText => Target.TextSnapshot;
        public string CellSnapshotText => Target.TextSnapshot;
        public int Row => SlideNumber;
        public int Column => ShapeId;
        public string DisplayLocation => Target.BuildDisplayLocation();
        public string SheetName => $"Slide {SlideNumber.ToString(CultureInfo.InvariantCulture)}";
        public string CellAddress => DisplayLocation;

        public string BuildContextSnippet(int radius = 26)
        {
            string snap = Target.TextSnapshot ?? string.Empty;
            if (string.IsNullOrEmpty(snap))
                return Issue?.OriginalText ?? string.Empty;

            // Keep character length 1-to-1 with PowerPoint TextRange (\r / \v -> single space)
            char[] buf = snap.ToCharArray();
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] == '\r' || buf[i] == '\n' || buf[i] == '\v' || buf[i] == '\t')
                    buf[i] = ' ';
            }
            string clean = new string(buf);

            int start = Math.Max(0, Math.Min(Issue.StartIndex, clean.Length));
            int len = Math.Max(0, Math.Min(Issue.Length, clean.Length - start));

            int left = Math.Max(0, start - radius);
            int right = Math.Min(clean.Length, start + len + radius);

            string prefix = left > 0 ? "…" + clean.Substring(left, start - left) : clean.Substring(0, start);
            string focus = len > 0 ? clean.Substring(start, len) : Issue.OriginalText;
            string suffix = right < clean.Length
                ? clean.Substring(start + len, right - (start + len)) + "…"
                : clean.Substring(start + len);

            return $"{prefix}【{focus}】{suffix}";
        }
    }

    /// <summary>
    /// 0-based character span edit planned for surgical PowerPoint <c>TextRange.Characters(StartZeroBased + 1, Length).Text = Replacement</c>
    /// execution in strictly descending offset order so run-level bold, size, RGB color, hyperlinks, and animations stay intact.
    /// </summary>
    public readonly struct PresentationSpanEdit
    {
        public int StartZeroBased { get; }
        public int Length { get; }
        public string Replacement { get; }

        public PresentationSpanEdit(int startZeroBased, int length, string replacement)
        {
            StartZeroBased = startZeroBased;
            Length = length;
            Replacement = replacement ?? string.Empty;
        }
    }

    public sealed class SlideTextWriteOperation
    {
        public PresentationTextTarget Target { get; set; } = new PresentationTextTarget();
        public string PreviousText { get; set; } = string.Empty;
        public string UpdatedText { get; set; } = string.Empty;
        public int ChangesInContainer { get; set; }
        public IReadOnlyList<PresentationSpanEdit> DescendingSpanEdits { get; set; } = Array.Empty<PresentationSpanEdit>();
        public string? PreviousFontName { get; set; }
        public string? TargetFontName { get; set; }
    }

    public sealed class SlideTextUndoEntry
    {
        public PresentationTextTarget Target { get; set; } = new PresentationTextTarget();
        public string PreviousText { get; set; } = string.Empty;
        public string NewText { get; set; } = string.Empty;
        public string? PreviousFontName { get; set; }
        public string? NewFontName { get; set; }
    }

    public sealed class PresentationUndoBatch
    {
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<SlideTextUndoEntry> Entries { get; set; } = new List<SlideTextUndoEntry>();
    }

    /// <summary>
    /// Bounded multi-level Undo history for PowerPoint Add-in write operations (complements PowerPoint's native Ctrl+Z).
    /// </summary>
    public sealed class PresentationUndoManager
    {
        private readonly int _maxBatches;
        private readonly LinkedList<PresentationUndoBatch> _history = new LinkedList<PresentationUndoBatch>();

        public PresentationUndoManager(int maxBatches = 30)
        {
            _maxBatches = Math.Max(1, maxBatches);
        }

        public int UndoCount => _history.Count;
        public bool CanUndo => _history.Count > 0;
        public string LatestDescription => _history.Last?.Value.Description ?? string.Empty;

        public void PushBatch(PresentationUndoBatch batch)
        {
            if (batch == null || batch.Entries == null || batch.Entries.Count == 0)
                return;

            while (_history.Count >= _maxBatches)
            {
                _history.RemoveFirst();
            }
            _history.AddLast(batch);
        }

        public void PushOperations(string description, IEnumerable<SlideTextWriteOperation> operations)
        {
            if (operations == null)
                return;

            var entries = new List<SlideTextUndoEntry>();
            foreach (var op in operations)
            {
                entries.Add(new SlideTextUndoEntry
                {
                    Target = op.Target.CloneWithText(op.UpdatedText, op.TargetFontName),
                    PreviousText = op.PreviousText,
                    NewText = op.UpdatedText,
                    PreviousFontName = op.PreviousFontName,
                    NewFontName = op.TargetFontName
                });
            }

            if (entries.Count > 0)
            {
                PushBatch(new PresentationUndoBatch
                {
                    Description = description,
                    Entries = entries
                });
            }
        }

        public PresentationUndoBatch? PopUndoBatch()
        {
            if (_history.Last == null)
                return null;

            PresentationUndoBatch top = _history.Last.Value;
            _history.RemoveLast();
            return top;
        }

        public void ClearForPresentation(string presentationName)
        {
            if (string.IsNullOrEmpty(presentationName))
                return;

            var node = _history.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.Entries.All(e => string.Equals(e.Target.PresentationName, presentationName, StringComparison.OrdinalIgnoreCase)))
                {
                    _history.Remove(node);
                }
                node = next;
            }
        }

        public void ClearAll() => _history.Clear();
    }

    public sealed class PresentationScanResult
    {
        public List<PresentationSlideIssue> Issues { get; set; } = new List<PresentationSlideIssue>();
        public DocumentQualityReport QualityReport { get; set; } = new DocumentQualityReport();
        public int ScannedContainerCount { get; set; }
        public int ScannedSlideCount { get; set; }
    }

    /// <summary>
    /// Pure, COM-free presentation proofing, descending 1-based character span planning,
    /// cross-slide consistency analysis, and slide typography toolkit helper.
    /// </summary>
    public static class PresentationProofingHelper
    {
        public static bool ShouldInspectContainerText(string? rawText, string? primaryFontName, out string text)
        {
            if (!string.IsNullOrWhiteSpace(rawText))
            {
                string s = rawText!;
                if (SpreadsheetProofingHelper.ContainsMyanmarScript(s) ||
                    LegacyMyanmarFontConverter.IsLegacyAsciiMyanmarFont(primaryFontName) ||
                    LegacyMyanmarFontConverter.IsZawgyiFont(primaryFontName))
                {
                    text = s;
                    return true;
                }
            }

            text = string.Empty;
            return false;
        }

        /// <summary>
        /// Normalizes PowerPoint vertical-tab soft line breaks (<c>\v</c>, U+000B) to <c>\n</c> of identical 1-char length
        /// so <see cref="MyanmarProofingEngine"/> sees clean clause boundaries while keeping exact 1-to-1 character offsets
        /// with <c>TextRange.Characters(start + 1, length)</c>.
        /// </summary>
        public static string NormalizePowerPointBreaksPreservingLength(string rawText)
        {
            if (string.IsNullOrEmpty(rawText) || rawText.IndexOf('\v') < 0)
                return rawText ?? string.Empty;

            return rawText.Replace('\v', '\n');
        }

        public static PresentationScanResult ScanTargets(
            MyanmarProofingEngine engine,
            IReadOnlyList<PresentationTextTarget> targets,
            bool includeCrossSlideConsistency = true,
            CancellationToken cancellationToken = default)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            var result = new PresentationScanResult();
            if (targets == null || targets.Count == 0)
                return result;

            var memo = new Dictionary<string, IReadOnlyList<ProofingIssue>>(StringComparer.Ordinal);
            var seenSlides = new HashSet<int>();
            var deckTextBuilder = new StringBuilder(Math.Min(65536, targets.Count * 96));
            var flatIssuesForQuality = new List<ProofingIssue>(128);

            for (int i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PresentationTextTarget target = targets[i];
                if (target.SlideNumber > 0)
                    seenSlides.Add(target.SlideNumber);

                if (!ShouldInspectContainerText(target.TextSnapshot, target.PrimaryFontName, out string raw))
                    continue;

                result.ScannedContainerCount++;
                if (deckTextBuilder.Length > 0)
                    deckTextBuilder.AppendLine();
                deckTextBuilder.Append(raw);

                // Detect legacy WinInnwa ASCII font containers upfront so users can convert them cleanly
                if (!SpreadsheetProofingHelper.ContainsMyanmarScript(raw) &&
                    LegacyMyanmarFontConverter.IsLegacyAsciiMyanmarFont(target.PrimaryFontName))
                {
                    string convertedPreview = LegacyMyanmarFontConverter.ConvertWinInnwaToUnicode(raw);
                    if (!string.Equals(convertedPreview, raw, StringComparison.Ordinal))
                    {
                        var winIssue = new ProofingIssue
                        {
                            StartIndex = 0,
                            Length = raw.Length,
                            OriginalText = raw,
                            IssueType = ProofingIssueType.ZawgyiEncodingError,
                            Message = UiLocalizer.Pick(
                                $"Legacy '{target.PrimaryFontName}' ASCII font detected — convert to Pyidaungsu Unicode.",
                                $"ရှေးသုံး '{target.PrimaryFontName}' ဖောင့် တွေ့ရှိရပါသည် — Pyidaungsu Unicode သို့ ပြောင်းလဲပါ။"),
                            GrammaticalReasoning = "WinInnwa / Legacy ASCII ဖောင့်မှ စံ Unicode 5.1+ သို့ ပြောင်းလဲရန် လိုအပ်ပါသည်။",
                            AuthoritativeSource = "Myanmar Unicode Standard (UTN #11 / Pyidaungsu)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = convertedPreview,
                                    MeaningCue = "Pyidaungsu Unicode",
                                    Score = 105.0,
                                    IsOfficialThatpon = true
                                }
                            }
                        };
                        result.Issues.Add(new PresentationSlideIssue { Target = target, Issue = winIssue });
                        flatIssuesForQuality.Add(winIssue);
                        continue;
                    }
                }

                string lengthAligned = NormalizePowerPointBreaksPreservingLength(raw);
                if (!memo.TryGetValue(lengthAligned, out IReadOnlyList<ProofingIssue>? containerIssues))
                {
                    containerIssues = engine.CheckText(lengthAligned);
                    if (memo.Count < 2048)
                        memo[lengthAligned] = containerIssues;
                }

                for (int k = 0; k < containerIssues.Count; k++)
                {
                    ProofingIssue cloned = CloneProofingIssueWithRawSlice(containerIssues[k], raw);
                    result.Issues.Add(new PresentationSlideIssue
                    {
                        Target = target,
                        Issue = cloned
                    });
                    flatIssuesForQuality.Add(cloned);
                }
            }

            result.ScannedSlideCount = seenSlides.Count;

            List<string>? consistencySummaries = null;
            if (includeCrossSlideConsistency && targets.Count >= 2 && deckTextBuilder.Length > 0)
            {
                // Cross-slide dual-valid spelling harmonization (e.g., ကော်မတီ vs ကော်မီတီ across slides)
                var variantCounts = new Dictionary<int, (int CountA, int CountB)>();
                for (int t = 0; t < targets.Count; t++)
                {
                    string txt = targets[t].TextSnapshot ?? string.Empty;
                    if (string.IsNullOrEmpty(txt))
                        continue;
                    for (int vp = 0; vp < DocumentConsistencyAnalyzer.VariantPairs.Length; vp++)
                    {
                        var spec = DocumentConsistencyAnalyzer.VariantPairs[vp];
                        int cA = CountOccurrencesOrdinal(txt, spec.FormA, spec.ForbiddenRightPrefixOnA);
                        int cB = CountOccurrencesOrdinal(txt, spec.FormB, spec.ForbiddenRightPrefixOnB);
                        if (cA > 0 || cB > 0)
                        {
                            variantCounts.TryGetValue(vp, out var prev);
                            variantCounts[vp] = (prev.CountA + cA, prev.CountB + cB);
                        }
                    }
                }

                consistencySummaries = new List<string>();
                foreach (var kv in variantCounts)
                {
                    if (kv.Value.CountA > 0 && kv.Value.CountB > 0)
                    {
                        var spec = DocumentConsistencyAnalyzer.VariantPairs[kv.Key];
                        bool preferA = kv.Value.CountA >= kv.Value.CountB;
                        string minority = preferA ? spec.FormB : spec.FormA;
                        string majority = preferA ? spec.FormA : spec.FormB;
                        consistencySummaries.Add(UiLocalizer.Pick(
                            $"{spec.CategoryEn}: mixed '{spec.FormA}' ({kv.Value.CountA}x) vs '{spec.FormB}' ({kv.Value.CountB}x) across slides -> prefer '{majority}'.",
                            $"{spec.CategoryMm}: ဆလိုက်များအကြား '{spec.FormA}' ({kv.Value.CountA} ကြိမ်) နှင့် '{spec.FormB}' ({kv.Value.CountB} ကြိမ်) ရောနှောနေသည် — '{majority}' ဖြင့် တစ်ပြေးညီညှိရန်။"));

                        for (int t = 0; t < targets.Count; t++)
                        {
                            PresentationTextTarget target = targets[t];
                            string txt = target.TextSnapshot ?? string.Empty;
                            int searchIdx = 0;
                            while ((searchIdx = txt.IndexOf(minority, searchIdx, StringComparison.Ordinal)) >= 0)
                            {
                                string? forbid = preferA ? spec.ForbiddenRightPrefixOnB : spec.ForbiddenRightPrefixOnA;
                                if (!string.IsNullOrEmpty(forbid) &&
                                    searchIdx + minority.Length + forbid!.Length <= txt.Length &&
                                    string.CompareOrdinal(txt, searchIdx + minority.Length, forbid, 0, forbid.Length) == 0)
                                {
                                    searchIdx += minority.Length;
                                    continue;
                                }

                                bool overlaps = false;
                                for (int ex = 0; ex < result.Issues.Count; ex++)
                                {
                                    var existing = result.Issues[ex];
                                    if (ReferenceEquals(existing.Target, target) &&
                                        Math.Max(searchIdx, existing.Issue.StartIndex) <
                                        Math.Min(searchIdx + minority.Length, existing.Issue.StartIndex + existing.Issue.Length))
                                    {
                                        overlaps = true;
                                        break;
                                    }
                                }

                                if (!overlaps)
                                {
                                    var cIssue = new ProofingIssue
                                    {
                                        StartIndex = searchIdx,
                                        Length = minority.Length,
                                        OriginalText = minority,
                                        IssueType = ProofingIssueType.ContextualMeaningWarning,
                                        RequiresReviewBeforeBatchFix = true,
                                        Message = UiLocalizer.Pick(
                                            $"Slide consistency: harmonize '{minority}' to majority form '{majority}'.",
                                            $"ဆလိုက်ရှေ့နောက်ညီညွတ်မှု — '{minority}' ကို အများသုံး '{majority}' နှင့် တစ်ပြေးညီညှိရန်။"),
                                        GrammaticalReasoning = UiLocalizer.Pick(
                                            $"Both '{spec.FormA}' and '{spec.FormB}' appear across this presentation; consistent slides use one standard spelling.",
                                            $"တင်ပြချက်တစ်ခုတည်းအတွင်း '{spec.FormA}' နှင့် '{spec.FormB}' ရောနှောသုံးစွဲထားသဖြင့် တစ်မျိုးတည်း တစ်ပြေးညီ သုံးနှုန်းသင့်ပါသည်။"),
                                        AuthoritativeSource = "Presentation Cross-Slide Consistency Check",
                                        Suggestions = new List<SuggestionCandidate>
                                        {
                                            new SuggestionCandidate
                                            {
                                                Replacement = majority,
                                                MeaningCue = UiLocalizer.Pick("Harmonize across slides", "ဆလိုက်တစ်ပြေးညီ စာလုံးပေါင်း"),
                                                Score = 96.0,
                                                IsOfficialThatpon = true
                                            }
                                        }
                                    };
                                    result.Issues.Add(new PresentationSlideIssue { Target = target, Issue = cIssue });
                                    flatIssuesForQuality.Add(cIssue);
                                }
                                searchIdx += minority.Length;
                            }
                        }
                    }
                }
            }

            result.QualityReport = DocumentQualityAnalyzer.Analyze(
                deckTextBuilder.ToString(),
                flatIssuesForQuality,
                engine,
                consistencySummaries);

            return result;
        }

        /// <summary>
        /// Plans non-overlapping span replacements in strictly DESCENDING <c>StartZeroBased</c> order for a single container,
        /// and produces the resulting snapshot string so callers can apply surgical 1-based <c>TextRange.Characters</c> edits
        /// without shifting earlier character offsets or destroying bold/color/size/animation runs.
        /// </summary>
        public static List<PresentationSpanEdit> ComputeDescendingSpanEdits(
            string originalSnapshot,
            IEnumerable<(int StartIndex, int Length, string Replacement)> requestedEdits,
            out string updatedSnapshot)
        {
            string baseText = originalSnapshot ?? string.Empty;
            var candidates = new List<PresentationSpanEdit>();
            if (requestedEdits != null)
            {
                foreach (var (st, len, rep) in requestedEdits)
                {
                    if (st < 0 || len <= 0 || st + len > baseText.Length || rep == null)
                        continue;

                    string curSlice = baseText.Substring(st, len);
                    if (string.Equals(curSlice, rep, StringComparison.Ordinal))
                        continue;

                    candidates.Add(new PresentationSpanEdit(st, len, rep));
                }
            }

            if (candidates.Count == 0)
            {
                updatedSnapshot = baseText;
                return candidates;
            }

            // Sort ascending first to filter out any overlapping spans deterministically, then reverse to descending
            candidates.Sort((a, b) =>
            {
                int cmp = a.StartZeroBased.CompareTo(b.StartZeroBased);
                return cmp != 0 ? cmp : b.Length.CompareTo(a.Length);
            });

            var nonOverlapping = new List<PresentationSpanEdit>(candidates.Count);
            int occupiedUntil = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var ed = candidates[i];
                if (ed.StartZeroBased < occupiedUntil)
                    continue;
                nonOverlapping.Add(ed);
                occupiedUntil = ed.StartZeroBased + ed.Length;
            }

            nonOverlapping.Sort((a, b) => b.StartZeroBased.CompareTo(a.StartZeroBased));

            string working = baseText;
            for (int i = 0; i < nonOverlapping.Count; i++)
            {
                var ed = nonOverlapping[i];
                working = working.Substring(0, ed.StartZeroBased) +
                          ed.Replacement +
                          working.Substring(ed.StartZeroBased + ed.Length);
            }

            updatedSnapshot = working;
            return nonOverlapping;
        }

        /// <summary>
        /// Adjusts remaining unresolved issue offsets inside the same container after a surgical replacement at <paramref name="editStart"/>.
        /// </summary>
        public static void ShiftSiblingIssueOffsetsAfterEdit(
            IList<PresentationSlideIssue> allIssues,
            PresentationTextTarget editedTarget,
            int editStart,
            int editOldLength,
            int editNewLength,
            string updatedSnapshotText)
        {
            if (allIssues == null || editedTarget == null)
                return;

            string targetKey = editedTarget.BuildTargetKey();
            int delta = editNewLength - editOldLength;
            int editEnd = editStart + editOldLength;

            for (int i = 0; i < allIssues.Count; i++)
            {
                PresentationSlideIssue item = allIssues[i];
                if (!string.Equals(item.Target.BuildTargetKey(), targetKey, StringComparison.Ordinal))
                    continue;

                item.Target.TextSnapshot = updatedSnapshotText;
                if (item.IsResolved)
                    continue;

                int iStart = item.Issue.StartIndex;
                int iEnd = iStart + item.Issue.Length;
                if (Math.Max(editStart, iStart) < Math.Min(editEnd, iEnd))
                {
                    item.IsResolved = true;
                }
                else if (iStart >= editEnd)
                {
                    item.Issue.StartIndex = iStart + delta;
                }
            }
        }

        public static List<SlideTextWriteOperation> PlanBatchIssueFixes(
            IReadOnlyDictionary<PresentationSlideIssue, string> selectedFixes)
        {
            var ops = new List<SlideTextWriteOperation>();
            if (selectedFixes == null || selectedFixes.Count == 0)
                return ops;

            var byContainer = selectedFixes
                .Where(kv => kv.Key != null && !kv.Key.IsResolved && kv.Value != null)
                .GroupBy(kv => kv.Key.Target.BuildTargetKey(), StringComparer.Ordinal);

            foreach (var grp in byContainer)
            {
                PresentationTextTarget target = grp.First().Key.Target;
                string prevText = target.TextSnapshot ?? string.Empty;
                var rawEdits = grp.Select(kv => (kv.Key.Issue.StartIndex, kv.Key.Issue.Length, kv.Value));
                List<PresentationSpanEdit> descEdits = ComputeDescendingSpanEdits(prevText, rawEdits, out string updatedText);
                if (descEdits.Count == 0 || string.Equals(prevText, updatedText, StringComparison.Ordinal))
                    continue;

                ops.Add(new SlideTextWriteOperation
                {
                    Target = target,
                    PreviousText = prevText,
                    UpdatedText = updatedText,
                    ChangesInContainer = descEdits.Count,
                    DescendingSpanEdits = descEdits,
                    PreviousFontName = target.PrimaryFontName,
                    TargetFontName = null
                });
            }

            return ops;
        }

        public static List<SlideTextWriteOperation> PlanLegacyFontToPyidaungsu(
            IReadOnlyList<PresentationTextTarget> targets)
        {
            var ops = new List<SlideTextWriteOperation>();
            if (targets == null)
                return ops;

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                string prev = target.TextSnapshot ?? string.Empty;
                if (string.IsNullOrWhiteSpace(prev))
                    continue;

                string converted = LegacyMyanmarFontConverter.ConvertSegmentToPyidaungsu(prev, target.PrimaryFontName, out bool wasConverted);
                bool needsFontUpgrade = (wasConverted || SpreadsheetProofingHelper.ContainsMyanmarScript(converted)) &&
                                        !string.Equals(target.PrimaryFontName, "Pyidaungsu", StringComparison.OrdinalIgnoreCase);

                if (wasConverted || needsFontUpgrade)
                {
                    var spanEdits = !string.Equals(prev, converted, StringComparison.Ordinal)
                        ? new List<PresentationSpanEdit> { new PresentationSpanEdit(0, prev.Length, converted) }
                        : new List<PresentationSpanEdit>();

                    ops.Add(new SlideTextWriteOperation
                    {
                        Target = target,
                        PreviousText = prev,
                        UpdatedText = converted,
                        ChangesInContainer = Math.Max(1, spanEdits.Count),
                        DescendingSpanEdits = spanEdits,
                        PreviousFontName = target.PrimaryFontName,
                        TargetFontName = "Pyidaungsu"
                    });
                }
            }

            return ops;
        }

        public static List<SlideTextWriteOperation> PlanSmartSlideZwsp(
            IReadOnlyList<PresentationTextTarget> targets,
            MyanmarTrie? trie,
            bool stripAllInsteadOfInject,
            out int totalZwspDelta)
        {
            totalZwspDelta = 0;
            var ops = new List<SlideTextWriteOperation>();
            if (targets == null)
                return ops;

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                string prev = target.TextSnapshot ?? string.Empty;
                if (string.IsNullOrWhiteSpace(prev) || !SpreadsheetProofingHelper.ContainsMyanmarScript(prev))
                    continue;

                string updated;
                int delta;
                if (stripAllInsteadOfInject)
                {
                    updated = MyanmarZwspToolkit.StripAllZeroWidthCharacters(prev, out int removed);
                    delta = removed;
                }
                else
                {
                    updated = MyanmarZwspToolkit.InsertSmartWordBoundaryZwsp(prev, trie, out int inserted, out int cleaned);
                    delta = inserted + cleaned;
                }

                if (!string.Equals(prev, updated, StringComparison.Ordinal))
                {
                    totalZwspDelta += Math.Max(1, delta);
                    ops.Add(new SlideTextWriteOperation
                    {
                        Target = target,
                        PreviousText = prev,
                        UpdatedText = updated,
                        ChangesInContainer = Math.Max(1, delta),
                        DescendingSpanEdits = new List<PresentationSpanEdit> { new PresentationSpanEdit(0, prev.Length, updated) },
                        PreviousFontName = target.PrimaryFontName
                    });
                }
            }

            return ops;
        }

        public static List<SlideTextWriteOperation> PlanOfficialSlideTypography(
            IReadOnlyList<PresentationTextTarget> targets,
            bool convertDigitsToMyanmar,
            out int totalChanges)
        {
            totalChanges = 0;
            var ops = new List<SlideTextWriteOperation>();
            if (targets == null)
                return ops;

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                string prev = target.TextSnapshot ?? string.Empty;
                if (string.IsNullOrWhiteSpace(prev) || !SpreadsheetProofingHelper.ContainsMyanmarScript(prev))
                    continue;

                string updated = MyanmarTypographyFormatter.FormatAllOfficialMyanmarTypography(
                    prev,
                    convertDigitsToMyanmar,
                    out TypographyFormatSummary summary);

                if (!string.Equals(prev, updated, StringComparison.Ordinal))
                {
                    int changes = Math.Max(1, summary.TotalChanges);
                    totalChanges += changes;
                    ops.Add(new SlideTextWriteOperation
                    {
                        Target = target,
                        PreviousText = prev,
                        UpdatedText = updated,
                        ChangesInContainer = changes,
                        DescendingSpanEdits = new List<PresentationSpanEdit> { new PresentationSpanEdit(0, prev.Length, updated) },
                        PreviousFontName = target.PrimaryFontName
                    });
                }
            }

            return ops;
        }

        public static List<SlideTextWriteOperation> PlanNumeralConversion(
            IReadOnlyList<PresentationTextTarget> targets,
            bool toMyanmarDigits,
            out int totalDigitsConverted)
        {
            totalDigitsConverted = 0;
            var ops = new List<SlideTextWriteOperation>();
            if (targets == null)
                return ops;

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                string prev = target.TextSnapshot ?? string.Empty;
                if (string.IsNullOrWhiteSpace(prev) || !SpreadsheetProofingHelper.ContainsMyanmarScript(prev))
                    continue;

                string updated = MyanmarTypographyFormatter.ConvertNumeralsSmart(prev, toMyanmarDigits, out int convertedCount);
                if (convertedCount > 0 && !string.Equals(prev, updated, StringComparison.Ordinal))
                {
                    totalDigitsConverted += convertedCount;
                    ops.Add(new SlideTextWriteOperation
                    {
                        Target = target,
                        PreviousText = prev,
                        UpdatedText = updated,
                        ChangesInContainer = convertedCount,
                        DescendingSpanEdits = new List<PresentationSpanEdit> { new PresentationSpanEdit(0, prev.Length, updated) },
                        PreviousFontName = target.PrimaryFontName
                    });
                }
            }

            return ops;
        }

        private static ProofingIssue CloneProofingIssueWithRawSlice(ProofingIssue src, string rawText)
        {
            var clonedSugs = new List<SuggestionCandidate>(src.Suggestions?.Count ?? 0);
            if (src.Suggestions != null)
            {
                for (int i = 0; i < src.Suggestions.Count; i++)
                {
                    var s = src.Suggestions[i];
                    clonedSugs.Add(new SuggestionCandidate
                    {
                        Replacement = s.Replacement,
                        MeaningCue = s.MeaningCue,
                        Score = s.Score,
                        PosTag = s.PosTag,
                        IsOfficialThatpon = s.IsOfficialThatpon,
                        SyllableBreakdown = s.SyllableBreakdown,
                        DiagnosticDetail = s.DiagnosticDetail,
                        HomophoneSiblings = s.HomophoneSiblings,
                        AbidanSummary = s.AbidanSummary
                    });
                }
            }

            int safeStart = Math.Max(0, Math.Min(src.StartIndex, rawText.Length));
            int safeLen = Math.Max(0, Math.Min(src.Length, rawText.Length - safeStart));
            string exactOrig = safeLen > 0 ? rawText.Substring(safeStart, safeLen) : src.OriginalText;

            return new ProofingIssue
            {
                StartIndex = safeStart,
                Length = safeLen,
                OriginalText = exactOrig,
                IssueType = src.IssueType,
                Message = src.Message,
                GrammaticalReasoning = src.GrammaticalReasoning,
                AuthoritativeSource = src.AuthoritativeSource,
                RequiresReviewBeforeBatchFix = src.RequiresReviewBeforeBatchFix,
                Suggestions = clonedSugs
            };
        }

        private static int CountOccurrencesOrdinal(string text, string pattern, string? forbiddenRightPrefix)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
                return 0;

            int count = 0;
            int idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
            {
                if (!string.IsNullOrEmpty(forbiddenRightPrefix) &&
                    idx + pattern.Length + forbiddenRightPrefix!.Length <= text.Length &&
                    string.CompareOrdinal(text, idx + pattern.Length, forbiddenRightPrefix, 0, forbiddenRightPrefix.Length) == 0)
                {
                    idx += pattern.Length;
                    continue;
                }
                count++;
                idx += pattern.Length;
            }
            return count;
        }
    }
}
