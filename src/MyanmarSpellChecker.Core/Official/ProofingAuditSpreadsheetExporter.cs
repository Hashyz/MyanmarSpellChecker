// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using MyanmarSpellChecker.Core.Localization;

namespace MyanmarSpellChecker.Core.Official
{
    /// <summary>
    /// Generates a 100% standards-compliant, styled OpenXML Excel Workbook (.xlsx) in pure .NET Framework 4.8
    /// (zero external NuGet packages and zero Excel COM marshalling overhead) so both Microsoft Word and Excel
    /// can export wide, color-coded, AutoFilter-enabled Pyidaungsu audit spreadsheets in under 15 ms.
    /// </summary>
    public static class ProofingAuditSpreadsheetExporter
    {
        // Style indices in xl/styles.xml cellXfs:
        private const int StyleNormalWrap = 0;
        private const int StyleBannerTitle = 1;
        private const int StyleBannerMeta = 2;
        private const int StyleKpiGreen = 3;
        private const int StyleKpiRed = 4;
        private const int StyleKpiYellow = 5;
        private const int StyleKpiBlue = 6;
        private const int StyleTableHeader = 7;
        private const int StyleCellCenterBold = 8;
        private const int StyleStatusWrongRed = 9;
        private const int StyleStatusReviewYellow = 10;
        private const int StyleWordWrongRed = 11;
        private const int StyleWordReviewYellow = 12;
        private const int StyleWordCorrectGreen = 13;

        private readonly struct CellSpec
        {
            public readonly string Text;
            public readonly int StyleId;

            public CellSpec(string? text, int styleId = StyleNormalWrap)
            {
                Text = SanitizeXmlText(text);
                StyleId = styleId;
            }
        }

        public static string SaveFriendlyXlsxToDisk(
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

            string stem = ProofingAuditReportBuilder.BuildSafeReportFileStem(model.DocumentTitle);
            string fileName = $"ProofingReport_{stem}_{model.GeneratedAtLocal:yyyyMMdd_HHmmss}.xlsx";
            string fullPath = Path.Combine(baseDir, fileName);

            byte[] xlsxBytes = BuildFriendlyXlsxBytes(model);
            File.WriteAllBytes(fullPath, xlsxBytes);
            return fullPath;
        }

        public static byte[] BuildFriendlyXlsxBytes(ProofingAuditReportModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            using (var ms = new MemoryStream(65536))
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteZipUtf8Entry(zip, "[Content_Types].xml", BuildContentTypesXml());
                    WriteZipUtf8Entry(zip, "_rels/.rels", BuildRootRelsXml());
                    WriteZipUtf8Entry(zip, "xl/workbook.xml", BuildWorkbookXml());
                    WriteZipUtf8Entry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRelsXml());
                    WriteZipUtf8Entry(zip, "xl/styles.xml", BuildStylesXml());
                    WriteZipUtf8Entry(zip, "xl/worksheets/sheet1.xml", BuildSheet1FriendlyChecklistXml(model));
                    WriteZipUtf8Entry(zip, "xl/worksheets/sheet2.xml", BuildSheet2SummaryAndRepeatedXml(model));
                    WriteZipUtf8Entry(zip, "xl/worksheets/sheet3.xml", BuildSheet3SessionFixedXml(model));
                }
                return ms.ToArray();
            }
        }

        private static void WriteZipUtf8Entry(ZipArchive zip, string entryPath, string xmlContent)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(xmlContent);
            }
        }

        private static string BuildContentTypesXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>";

        private static string BuildRootRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private static string BuildWorkbookXml()
        {
            string s1 = EscapeAttr(UiLocalizer.Pick("1_Friendly_Review", "၁_စာမျက်နှာနှင့်လိုင်းအလိုက်"));
            string s2 = EscapeAttr(UiLocalizer.Pick("2_Scorecard_&_Repeated", "၂_အနှစ်ချုပ်နှင့်ထပ်ခါအမှား"));
            string s3 = EscapeAttr(UiLocalizer.Pick("3_Session_Fixed", "၃_ယခုပြင်ဆင်ပြီးစာရင်း"));

            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                   "<sheets>" +
                   $"<sheet name=\"{s1}\" sheetId=\"1\" r:id=\"rId1\"/>" +
                   $"<sheet name=\"{s2}\" sheetId=\"2\" r:id=\"rId2\"/>" +
                   $"<sheet name=\"{s3}\" sheetId=\"3\" r:id=\"rId3\"/>" +
                   "</sheets>" +
                   "</workbook>";
        }

        private static string BuildWorkbookRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
            "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet3.xml\"/>" +
            "<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        private static string BuildStylesXml()
        {
            // Fonts:
            // 0 = Regular 11pt Pyidaungsu (#1E293B)
            // 1 = Title Bold 13.5pt White (#FFFFFF)
            // 2 = Meta Regular 10.5pt Slate (#334155)
            // 3 = Bold 11pt Emerald (#106C38)
            // 4 = Bold 11pt Crimson (#BE1E2D)
            // 5 = Bold 11pt Dark Yellow-Gold (#855A00)
            // 6 = Bold 11pt Navy/Blue (#102A48)
            // 7 = Header Bold 11pt White (#FFFFFF)
            // 8 = Bold 11pt Dark (#1E293B)
            // Fills (0=none, 1=gray125 mandatory in Excel):
            // 2 = Navy Banner (#102A48)
            // 3 = Soft Ice Meta (#F1F5F9)
            // 4 = Soft Green (#E4F7EB)
            // 5 = Soft Red (#FDECEC)
            // 6 = Canary Yellow (#FEF9C3)
            // 7 = Soft Blue (#E8F1FD)
            // 8 = Table Header Slate-Navy (#1B3B64)
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                   "<fonts count=\"9\">" +
                   "<font><sz val=\"11\"/><color rgb=\"FF1E293B\"/><name val=\"Pyidaungsu\"/></font>" +
                   "<font><b/><sz val=\"13.5\"/><color rgb=\"FFFFFFFF\"/><name val=\"Pyidaungsu\"/></font>" +
                   "<font><sz val=\"10.5\"/><color rgb=\"FF334155\"/><name val=\"Pyidaungsu\"/></font>" +
                   "<font><b/><sz val=\"11\"/><color rgb=\"FF106C38\"/><name val=\"Pyidaungsu\"/></font>" +
                   "<font><b/><sz val=\"11\"/><color rgb=\"FFBE1E2D\"/><name val=\"Pyidaungsu\"/></font>" +
                   "<font><b/><sz val=\"11\"/><color rgb=\"FF855A00\"/><name val=\"Pyidaungsu\"/></font>" +
                   "<font><b/><sz val=\"11\"/><color rgb=\"FF102A48\"/><name val=\"Pyidaungsu\"/></font>" +
                   "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Pyidaungsu\"/></font>" +
                   "<font><b/><sz val=\"11\"/><color rgb=\"FF1E293B\"/><name val=\"Pyidaungsu\"/></font>" +
                   "</fonts>" +
                   "<fills count=\"9\">" +
                   "<fill><patternFill patternType=\"none\"/></fill>" +
                   "<fill><patternFill patternType=\"gray125\"/></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF102A48\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF1F5F9\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE4F7EB\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFDECEC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFEF9C3\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE8F1FD\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1B3B64\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "</fills>" +
                   "<borders count=\"2\">" +
                   "<border><left/><right/><top/><bottom/><diagonal/></border>" +
                   "<border>" +
                   "<left style=\"thin\"><color rgb=\"FFCBD5E1\"/></left>" +
                   "<right style=\"thin\"><color rgb=\"FFCBD5E1\"/></right>" +
                   "<top style=\"thin\"><color rgb=\"FFCBD5E1\"/></top>" +
                   "<bottom style=\"thin\"><color rgb=\"FFCBD5E1\"/></bottom>" +
                   "<diagonal/>" +
                   "</border>" +
                   "</borders>" +
                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                   "<cellXfs count=\"14\">" +
                   // 0: StyleNormalWrap
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                   // 1: StyleBannerTitle
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
                   // 2: StyleBannerMeta
                   "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
                   // 3: StyleKpiGreen
                   "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
                   // 4: StyleKpiRed
                   "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"5\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
                   // 5: StyleKpiYellow
                   "<xf numFmtId=\"0\" fontId=\"5\" fillId=\"6\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
                   // 6: StyleKpiBlue
                   "<xf numFmtId=\"0\" fontId=\"6\" fillId=\"7\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
                   // 7: StyleTableHeader
                   "<xf numFmtId=\"0\" fontId=\"7\" fillId=\"8\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
                   // 8: StyleCellCenterBold
                   "<xf numFmtId=\"0\" fontId=\"8\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"top\" wrapText=\"1\"/></xf>" +
                   // 9: StyleStatusWrongRed
                   "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"5\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                   // 10: StyleStatusReviewYellow
                   "<xf numFmtId=\"0\" fontId=\"5\" fillId=\"6\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                   // 11: StyleWordWrongRed
                   "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                   // 12: StyleWordReviewYellow
                   "<xf numFmtId=\"0\" fontId=\"5\" fillId=\"6\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                   // 13: StyleWordCorrectGreen
                   "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                   "</cellXfs>" +
                   "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                   "</styleSheet>";
        }

        private static string BuildSheet1FriendlyChecklistXml(ProofingAuditReportModel model)
        {
            var rows = new List<(int RowNumber, double? CustomHeight, CellSpec[] Cells)>(model.AllItemsInDocumentOrder.Count + 8);

            // Row 1: Friendly Title Banner (Merged A1:H1)
            string titleText = UiLocalizer.Pick(
                $"MYANMAR PROOFING AUDIT REPORT — {model.DocumentTitle} ({model.HostAppLabel})",
                $"မြန်မာစာ စာလုံးပေါင်းနှင့် သဒ္ဒါ စစ်ဆေးချက် အစီရင်ခံစာ — {model.DocumentTitle} ({model.HostAppLabel})");
            rows.Add((1, 30.0, PadRow(8, StyleBannerTitle, new CellSpec(titleText, StyleBannerTitle))));

            // Row 2: Checked Date & Mode (Merged A2:H2)
            string metaText = $"{model.OfficialDateHeader}   •   {model.StyleModeLabel}   •   {UiLocalizer.Pick("Total Words:", "စကားလုံးပေါင်း -")} {model.TotalWords:N0} ({model.MyanmarSyllables:N0} syllables)";
            rows.Add((2, 22.0, PadRow(8, StyleBannerMeta, new CellSpec(metaText, StyleBannerMeta))));

            // Row 3: 4 Visual KPI Cards across A3:B3, C3:D3, E3:F3, G3:H3
            string kpiClean = UiLocalizer.Pick(
                $"✓ Clean Accuracy: {model.CleanAccuracyPercent:0.##}% (Grade {model.GradeCode})",
                $"✓ အမှားကင်းစင်မှု: {model.CleanAccuracyPercent:0.##}% (အဆင့် {model.GradeCode})");
            string kpiWrong = UiLocalizer.Pick(
                $"Confirmed Wrong (Red): {model.ConfirmedWrongCount:N0} ({model.ConfirmedWrongRatePercent:0.##}%)",
                $"သေချာသောအမှား (အနီ): {model.ConfirmedWrongCount:N0} ({model.ConfirmedWrongRatePercent:0.##}%)");
            string kpiSusp = UiLocalizer.Pick(
                $"⚠ Review Needed (Yellow): {model.SuspiciousReviewCount:N0} ({model.SuspiciousRatePercent:0.##}%)",
                $"⚠ စိစစ်ရန် သံသယရှိ (အဝါ): {model.SuspiciousReviewCount:N0} ({model.SuspiciousRatePercent:0.##}%)");
            string kpiFixed = UiLocalizer.Pick(
                $"✓ Fixed in Session: {model.SessionFixedCount:N0}",
                $"✓ ယခုပြင်ဆင်ပြီး: {model.SessionFixedCount:N0}");

            rows.Add((3, 26.0, new[]
            {
                new CellSpec(kpiClean, StyleKpiGreen), new CellSpec(string.Empty, StyleKpiGreen),
                new CellSpec(kpiWrong, StyleKpiRed),   new CellSpec(string.Empty, StyleKpiRed),
                new CellSpec(kpiSusp, StyleKpiYellow), new CellSpec(string.Empty, StyleKpiYellow),
                new CellSpec(kpiFixed, StyleKpiBlue),  new CellSpec(string.Empty, StyleKpiBlue)
            }));

            // Row 5: Frozen AutoFilter Table Header
            rows.Add((5, 28.0, new[]
            {
                new CellSpec("#", StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Page / Sheet", "စာမျက်နှာ / Sheet"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Paragraph / Section", "စာပိုဒ် / ဇယား / အကွက်"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Line", "လိုင်း"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Status", "အခြေအနေ"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Found in Document", "တွေ့ရှိသောစာသား"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("How It Should Be", "အမှန်ရေးရမည့်စာသား"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Sentence Context (〖Word〗) & Simple Rule", "ဝါကျအတွင်း တည်နေရာ (〖စာလုံး〗) နှင့် ရှင်းလင်းချက်"), StyleTableHeader)
            }));

            int currentRow = 6;
            if (model.AllItemsInDocumentOrder.Count == 0)
            {
                rows.Add((currentRow++, 26.0, new[]
                {
                    new CellSpec("✓", StyleKpiGreen),
                    new CellSpec("-", StyleNormalWrap),
                    new CellSpec("-", StyleNormalWrap),
                    new CellSpec("-", StyleNormalWrap),
                    new CellSpec(UiLocalizer.Pick("100% Clean", "အမှားကင်းစင်"), StyleKpiGreen),
                    new CellSpec(UiLocalizer.Pick("Zero unresolved issues!", "ပြင်ဆင်ရန် အမှားမရှိပါ"), StyleKpiGreen),
                    new CellSpec("-", StyleNormalWrap),
                    new CellSpec(UiLocalizer.Pick("All checked text passed spelling & grammar verification.", "စာလုံးပေါင်းနှင့် သဒ္ဒါ စစ်ဆေးချက် အားလုံး ကောင်းမွန်ပါသည်။"), StyleNormalWrap)
                }));
            }
            else
            {
                foreach (var r in model.AllItemsInDocumentOrder)
                {
                    int statusStyle = r.IsConfirmedWrong ? StyleStatusWrongRed : StyleStatusReviewYellow;
                    int origStyle = r.IsConfirmedWrong ? StyleWordWrongRed : StyleWordReviewYellow;
                    string ctx = !string.IsNullOrEmpty(r.MarkedContextSnippet) ? r.MarkedContextSnippet : r.ContextSnippet;
                    string combinedNote = string.IsNullOrWhiteSpace(r.Explanation)
                        ? ctx
                        : $"{ctx}\n• {r.Explanation}";

                    rows.Add((currentRow++, null, new[]
                    {
                        new CellSpec(r.DocumentOrderIndex.ToString(CultureInfo.InvariantCulture), StyleCellCenterBold),
                        new CellSpec(r.PageOrSheetLabel, StyleCellCenterBold),
                        new CellSpec(r.ParagraphOrCellLabel, StyleNormalWrap),
                        new CellSpec(r.LineLabel, StyleCellCenterBold),
                        new CellSpec($"{r.StatusBadge}\n[{r.CategoryLabel}]", statusStyle),
                        new CellSpec(r.VisualOriginal, origStyle),
                        new CellSpec(r.HowItShouldBeDisplay, StyleWordCorrectGreen),
                        new CellSpec(combinedNote, StyleNormalWrap)
                    }));
                }
            }

            int lastFilterRow = Math.Max(6, currentRow - 1);
            var merges = new[] { "A1:H1", "A2:H2", "A3:B3", "C3:D3", "E3:F3", "G3:H3" };
            double[] widths = { 6.5, 14.0, 24.0, 12.0, 24.0, 24.0, 28.0, 68.0 };

            return BuildWorksheetXml(
                tabSelected: true,
                frozenRows: 5,
                colWidths: widths,
                rows: rows,
                mergedRanges: merges,
                autoFilterRef: $"A5:H{lastFilterRow}");
        }

        private static string BuildSheet2SummaryAndRepeatedXml(ProofingAuditReportModel model)
        {
            var rows = new List<(int RowNumber, double? CustomHeight, CellSpec[] Cells)>(model.TopRepeatedGroups.Count + 20);

            rows.Add((1, 28.0, PadRow(6, StyleBannerTitle, new CellSpec(
                UiLocalizer.Pick("4-PART WORD HEALTH SCORECARD & TOP REPEATED WORDS", "စကားလုံး အရည်အသွေး အနှစ်ချုပ် နှင့် ထပ်ခါတလဲလဲ တွေ့ရှိချက်များ"),
                StyleBannerTitle))));

            rows.Add((3, 24.0, new[]
            {
                new CellSpec(UiLocalizer.Pick("Metric", "တိုင်းတာချက်"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Value", "ရလဒ်"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Details", "အသေးစိတ်"), StyleTableHeader),
                new CellSpec(string.Empty, StyleTableHeader),
                new CellSpec(string.Empty, StyleTableHeader),
                new CellSpec(string.Empty, StyleTableHeader)
            }));

            int rNo = 4;
            void AddScoreRow(string metric, string val, string detail, int valStyle)
            {
                rows.Add((rNo++, 22.0, new[]
                {
                    new CellSpec(metric, StyleNormalWrap),
                    new CellSpec(val, valStyle),
                    new CellSpec(detail, StyleNormalWrap),
                    new CellSpec(string.Empty, StyleNormalWrap),
                    new CellSpec(string.Empty, StyleNormalWrap),
                    new CellSpec(string.Empty, StyleNormalWrap)
                }));
            }

            AddScoreRow(
                UiLocalizer.Pick("Overall Quality Score", "အရည်အသွေး စုစုပေါင်းရမှတ်"),
                $"{model.OverallScore}% ({model.GradeCode})",
                model.LocalizedGradeLabel,
                StyleKpiGreen);
            AddScoreRow(
                UiLocalizer.Pick("Total Words / Syllables", "စုစုပေါင်း စကားလုံး / ဝဏ္ဏ"),
                $"{model.TotalWords:N0} words",
                $"{model.MyanmarSyllables:N0} syllables • Clean: {model.CleanAccuracyPercent:0.##}% (~{model.EstimatedCleanWords:N0} words)",
                StyleKpiBlue);
            AddScoreRow(
                UiLocalizer.Pick("Confirmed Wrong (Red)", "သေချာသောအမှား (အနီ)"),
                $"{model.ConfirmedWrongCount:N0} ({model.ConfirmedWrongRatePercent:0.##}%)",
                $"Spelling: {model.ConfirmedSpellingCount}  |  Unicode/Lookalike/ZWSP: {model.ConfirmedEncodingAndLookalikeCount}  |  Grammar/Style: {model.ConfirmedGrammarAndStyleCount}",
                StyleKpiRed);
            AddScoreRow(
                UiLocalizer.Pick("Review Needed (Yellow)", "စိစစ်ရန် သံသယရှိ (အဝါ)"),
                $"{model.SuspiciousReviewCount:N0} ({model.SuspiciousRatePercent:0.##}%)",
                $"Names/Guesses: {model.SuspiciousNameOrOovCount}  |  Context Meaning: {model.SuspiciousContextMeaningCount}  |  Consistency: {model.ConsistencyWarningCount}",
                StyleKpiYellow);
            AddScoreRow(
                UiLocalizer.Pick("Already Fixed in Session", "ယခုအကြိမ် ပြင်ဆင်ပြီးအရေအတွက်"),
                $"{model.SessionFixedCount:N0}",
                UiLocalizer.Pick("Logged in Tab 3 (3_Session_Fixed)", "Tab 3 တွင် အသေးစိတ်ကြည့်ရှုနိုင်သည်"),
                StyleKpiGreen);

            rNo++;
            int repHeaderRow = rNo;
            rows.Add((rNo++, 26.0, new[]
            {
                new CellSpec("#", StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Status", "အခြေအနေ"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Found Word", "တွေ့ရှိသောစာသား"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Count", "အကြိမ်ရေ"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("How It Should Be", "အမှန်ရေးရမည့်စာသား"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Locations (Page • Para, Line)", "တွေ့ရှိသည့် နေရာများ"), StyleTableHeader)
            }));

            for (int i = 0; i < model.TopRepeatedGroups.Count; i++)
            {
                var g = model.TopRepeatedGroups[i];
                int stStyle = g.IsConfirmedWrong ? StyleStatusWrongRed : StyleStatusReviewYellow;
                int wdStyle = g.IsConfirmedWrong ? StyleWordWrongRed : StyleWordReviewYellow;
                rows.Add((rNo++, null, new[]
                {
                    new CellSpec((i + 1).ToString(CultureInfo.InvariantCulture), StyleCellCenterBold),
                    new CellSpec(g.StatusBadge, stStyle),
                    new CellSpec(g.VisualOriginal, wdStyle),
                    new CellSpec($"×{g.Count}", StyleCellCenterBold),
                    new CellSpec(g.RecommendedFix, StyleWordCorrectGreen),
                    new CellSpec(g.SampleLocations, StyleNormalWrap)
                }));
            }

            var merges = new List<string> { "A1:F1", "C3:F3", "C4:F4", "C5:F5", "C6:F6", "C7:F7", "C8:F8" };
            double[] widths = { 7.0, 24.0, 26.0, 11.0, 28.0, 56.0 };
            int lastRow = Math.Max(repHeaderRow, rNo - 1);

            return BuildWorksheetXml(
                tabSelected: false,
                frozenRows: 0,
                colWidths: widths,
                rows: rows,
                mergedRanges: merges,
                autoFilterRef: model.TopRepeatedGroups.Count > 0 ? $"A{repHeaderRow}:F{lastRow}" : null);
        }

        private static string BuildSheet3SessionFixedXml(ProofingAuditReportModel model)
        {
            var rows = new List<(int RowNumber, double? CustomHeight, CellSpec[] Cells)>(model.SessionFixedRecords.Count + 6);

            rows.Add((1, 28.0, PadRow(5, StyleBannerTitle, new CellSpec(
                UiLocalizer.Pick($"ALREADY FIXED IN THIS SESSION ({model.SessionFixedCount})", $"ယခုအကြိမ် ပြင်ဆင်ပြီးစီးခဲ့သော မှတ်တမ်း ({model.SessionFixedCount} ခု)"),
                StyleBannerTitle))));

            rows.Add((3, 26.0, new[]
            {
                new CellSpec("#", StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Location (Page • Para, Line)", "ပြင်ဆင်ခဲ့သည့်နေရာ"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Original Text", "မူလစာသား"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Applied Fix", "ပြင်ဆင်ပြီးစာသား"), StyleTableHeader),
                new CellSpec(UiLocalizer.Pick("Time", "အချိန်"), StyleTableHeader)
            }));

            int rNo = 4;
            if (model.SessionFixedRecords.Count == 0)
            {
                rows.Add((rNo++, 24.0, new[]
                {
                    new CellSpec("-", StyleCellCenterBold),
                    new CellSpec(UiLocalizer.Pick("No fixes applied yet in this session.", "ယခုအကြိမ်အတွင်း ပြင်ဆင်ထားသောမှတ်တမ်း မရှိသေးပါ။"), StyleNormalWrap),
                    new CellSpec("-", StyleNormalWrap),
                    new CellSpec("-", StyleNormalWrap),
                    new CellSpec("-", StyleNormalWrap)
                }));
            }
            else
            {
                for (int i = 0; i < model.SessionFixedRecords.Count; i++)
                {
                    var f = model.SessionFixedRecords[i];
                    rows.Add((rNo++, 22.0, new[]
                    {
                        new CellSpec((i + 1).ToString(CultureInfo.InvariantCulture), StyleCellCenterBold),
                        new CellSpec(f.LocationLabel, StyleNormalWrap),
                        new CellSpec(f.VisualOriginalText, StyleWordWrongRed),
                        new CellSpec($"✓ {f.AppliedReplacement}", StyleWordCorrectGreen),
                        new CellSpec(f.AppliedAtLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture), StyleCellCenterBold)
                    }));
                }
            }

            double[] widths = { 7.0, 34.0, 28.0, 28.0, 14.0 };
            return BuildWorksheetXml(
                tabSelected: false,
                frozenRows: 3,
                colWidths: widths,
                rows: rows,
                mergedRanges: new[] { "A1:E1" },
                autoFilterRef: model.SessionFixedRecords.Count > 0 ? $"A3:E{rNo - 1}" : null);
        }

        private static CellSpec[] PadRow(int totalCols, int fillStyle, CellSpec first)
        {
            var arr = new CellSpec[totalCols];
            arr[0] = first;
            for (int i = 1; i < totalCols; i++)
                arr[i] = new CellSpec(string.Empty, fillStyle);
            return arr;
        }

        private static string BuildWorksheetXml(
            bool tabSelected,
            int frozenRows,
            double[] colWidths,
            List<(int RowNumber, double? CustomHeight, CellSpec[] Cells)> rows,
            IReadOnlyCollection<string>? mergedRanges,
            string? autoFilterRef)
        {
            var sb = new StringBuilder(32768);
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            // 1. sheetViews
            sb.Append("<sheetViews>");
            sb.Append($"<sheetView tabSelected=\"{(tabSelected ? "1" : "0")}\" workbookViewId=\"0\">");
            if (frozenRows > 0)
            {
                sb.Append($"<pane ySplit=\"{frozenRows}\" topLeftCell=\"A{frozenRows + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/>");
            }
            sb.Append("</sheetView></sheetViews>");

            // 2. sheetFormatPr
            sb.Append("<sheetFormatPr defaultRowHeight=\"22\"/>");

            // 3. cols
            sb.Append("<cols>");
            for (int c = 0; c < colWidths.Length; c++)
            {
                int colIdx1 = c + 1;
                string w = colWidths[c].ToString("0.##", CultureInfo.InvariantCulture);
                sb.Append($"<col min=\"{colIdx1}\" max=\"{colIdx1}\" width=\"{w}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");

            // 4. sheetData
            sb.Append("<sheetData>");
            foreach (var row in rows)
            {
                if (row.CustomHeight.HasValue)
                {
                    string ht = row.CustomHeight.Value.ToString("0.##", CultureInfo.InvariantCulture);
                    sb.Append($"<row r=\"{row.RowNumber}\" ht=\"{ht}\" customHeight=\"1\">");
                }
                else
                {
                    sb.Append($"<row r=\"{row.RowNumber}\">");
                }

                for (int c = 0; c < row.Cells.Length; c++)
                {
                    string cellRef = GetExcelColumnName(c + 1) + row.RowNumber.ToString(CultureInfo.InvariantCulture);
                    CellSpec cell = row.Cells[c];
                    string escaped = SecurityElement.Escape(cell.Text) ?? string.Empty;
                    sb.Append($"<c r=\"{cellRef}\" s=\"{cell.StyleId}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{escaped}</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");

            // 5. autoFilter MUST precede mergeCells in OpenXML CT_Worksheet sequence!
            if (!string.IsNullOrWhiteSpace(autoFilterRef))
            {
                sb.Append($"<autoFilter ref=\"{autoFilterRef}\"/>");
            }

            // 6. mergeCells
            if (mergedRanges != null && mergedRanges.Count > 0)
            {
                sb.Append($"<mergeCells count=\"{mergedRanges.Count}\">");
                foreach (string mr in mergedRanges)
                {
                    sb.Append($"<mergeCell ref=\"{mr}\"/>");
                }
                sb.Append("</mergeCells>");
            }

            // 7. pageSetup (Landscape A4 for friendly printing straight from Excel)
            sb.Append("<pageSetup paperSize=\"9\" orientation=\"landscape\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static string GetExcelColumnName(int oneBasedColumn)
        {
            int dividend = oneBasedColumn;
            string colName = string.Empty;
            while (dividend > 0)
            {
                int mod = (dividend - 1) % 26;
                colName = Convert.ToChar('A' + mod) + colName;
                dividend = (dividend - mod) / 26;
            }
            return colName;
        }

        private static string EscapeAttr(string s) =>
            (SecurityElement.Escape(SanitizeXmlText(s)) ?? "Sheet").Replace(":", "_").Replace("/", "_").Replace("\\", "_").Replace("?", "").Replace("*", "").Replace("[", "(").Replace("]", ")");

        private static string SanitizeXmlText(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var sb = new StringBuilder(input!.Length);
            foreach (char ch in input)
            {
                if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD))
                    sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}

