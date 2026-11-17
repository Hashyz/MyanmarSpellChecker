// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using MyanmarSpellChecker.Core.Localization;

namespace MyanmarSpellChecker.Core.Official
{
    /// <summary>
    /// Generates a 100% standards-compliant, print-ready ISO A4 Portrait (210 x 297 mm) Microsoft Word (.docx)
    /// Proofing &amp; Orthography Audit Report in pure .NET Framework 4.8 (zero external NuGet packages and zero Word COM required).
    /// Works identically from Microsoft Word, Microsoft Excel, and Microsoft PowerPoint in under 20 ms:
    /// - True ISO A4 Portrait geometry (11906 x 16838 dxa) with 14 mm margins (10318 dxa printable width)
    /// - Repeating table headers (&lt;w:tblHeader/&gt;) on every printed A4 page &amp; non-splitting rows (&lt;w:cantSplit/&gt;)
    /// - Running A4 Header + Official Date &amp; live 'Page X of Y' Footer
    /// - Built-in &lt;w:noProof/&gt; + &lt;w:hideSpellingErrors/&gt; so Word never draws distracting red squiggles inside the report
    /// - Smart Auto-Scaling (&lt;= 250 items: full row-by-row tables; &gt; 250 items: 100% of Unique Grouped Findings + top 250 compact rows)
    /// - All 4 Logical Sections + Per-Sheet/Slide/Page Breakdown + Pen-Check (☐) boxes + Official Reviewer Sign-Off Block.
    /// </summary>
    public static class ProofingAuditWordDocumentExporter
    {
        public const int IsoA4PortraitWidthTwips = 11906;   // 210.0 mm
        public const int IsoA4PortraitHeightTwips = 16838;  // 297.0 mm
        public const int DefaultSmartCompactRowCap = 250;

        private const int MarginTopTwips = 850;             // ~15.0 mm
        private const int MarginBottomTwips = 850;          // ~15.0 mm
        private const int MarginSideTwips = 794;            // ~14.0 mm
        private const int ContentWidthTwips = IsoA4PortraitWidthTwips - (MarginSideTwips * 2); // 10,318 twips (~182 mm)

        public static string SavePrintableA4DocxToDisk(
            ProofingAuditReportModel model,
            string? outputDirectoryOverride = null,
            int compactRowCap = DefaultSmartCompactRowCap)
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
            string fileName = $"ProofingReport_A4_{stem}_{model.GeneratedAtLocal:yyyyMMdd_HHmmss}.docx";
            string fullPath = Path.Combine(baseDir, fileName);

            byte[] docxBytes = BuildPrintableA4DocxBytes(model, compactRowCap);
            File.WriteAllBytes(fullPath, docxBytes);
            return fullPath;
        }

        public static byte[] BuildPrintableA4DocxBytes(
            ProofingAuditReportModel model,
            int compactRowCap = DefaultSmartCompactRowCap)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            int effectiveCap = Math.Max(50, compactRowCap);

            using (var ms = new MemoryStream(98304))
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteZipUtf8Entry(zip, "[Content_Types].xml", BuildContentTypesXml());
                    WriteZipUtf8Entry(zip, "_rels/.rels", BuildRootRelsXml());
                    WriteZipUtf8Entry(zip, "docProps/core.xml", BuildCorePropsXml(model));
                    WriteZipUtf8Entry(zip, "word/_rels/document.xml.rels", BuildDocumentRelsXml());
                    WriteZipUtf8Entry(zip, "word/styles.xml", BuildStylesXml());
                    WriteZipUtf8Entry(zip, "word/settings.xml", BuildSettingsXml());
                    WriteZipUtf8Entry(zip, "word/header1.xml", BuildHeaderXml(model));
                    WriteZipUtf8Entry(zip, "word/footer1.xml", BuildFooterXml(model));
                    WriteZipUtf8Entry(zip, "word/document.xml", BuildDocumentXml(model, effectiveCap));
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

        private static string E(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var sb = new StringBuilder(raw!.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && !(c >= 0xD800 && c <= 0xDFFF) && c != 0xFFFE && c != 0xFFFF))
                {
                    sb.Append(c);
                }
            }
            return SecurityElement.Escape(sb.ToString()) ?? string.Empty;
        }

        private static string BuildContentTypesXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
            "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
            "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>" +
            "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
            "</Types>";

        private static string BuildRootRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
            "</Relationships>";

        private static string BuildDocumentRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
            "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>" +
            "<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>" +
            "</Relationships>";

        private static string BuildCorePropsXml(ProofingAuditReportModel model)
        {
            string utcIso = model.GeneratedAtLocal.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                   "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                   "xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                   "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                   $"<dc:title>{E(model.DocumentTitle)} — Myanmar Proofing A4 Audit Report</dc:title>" +
                   "<dc:creator>Myanmar Official Spell Checker — Htet Yar Zar (Kyaukse University)</dc:creator>" +
                   "<dc:description>100% Offline Myanmar Spelling, Grammar &amp; Orthography A4 Audit Report</dc:description>" +
                   $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{utcIso}</dcterms:created>" +
                   $"<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{utcIso}</dcterms:modified>" +
                   "</cp:coreProperties>";
        }

        private static string BuildStylesXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:docDefaults>" +
            "<w:rPrDefault><w:rPr>" +
            "<w:rFonts w:ascii=\"Pyidaungsu\" w:hAnsi=\"Pyidaungsu\" w:eastAsia=\"Pyidaungsu\" w:cs=\"Pyidaungsu\"/>" +
            "<w:noProof/>" +
            "<w:color w:val=\"1E293B\"/>" +
            "<w:sz w:val=\"19\"/><w:szCs w:val=\"19\"/>" +
            "</w:rPr></w:rPrDefault>" +
            "<w:pPrDefault><w:pPr><w:spacing w:after=\"40\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault>" +
            "</w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            "</w:styles>";

        private static string BuildSettingsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:hideSpellingErrors/>" +
            "<w:hideGrammaticalErrors/>" +
            "</w:settings>";

        private static string BuildHeaderXml(ProofingAuditReportModel model)
        {
            string hdrRight = UiLocalizer.Pick("A4 Printable Proofing Audit Report", "A4 ပုံနှိပ် စာလုံးပေါင်းစစ်ဆေးချက် အစီရင်ခံစာ");
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                   "<w:p>" +
                   "<w:pPr>" +
                   "<w:pBdr><w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"4\" w:color=\"CBD5E1\"/></w:pBdr>" +
                   $"<w:tabs><w:tab w:val=\"right\" w:pos=\"{ContentWidthTwips}\"/></w:tabs>" +
                   "<w:spacing w:after=\"60\"/>" +
                   "</w:pPr>" +
                   BuildRun($"{model.DocumentTitle} ({model.HostAppLabel})", 16, "475569", bold: true) +
                   "<w:r><w:tab/></w:r>" +
                   BuildRun(hdrRight, 16, "64748B", bold: false) +
                   "</w:p>" +
                   "</w:hdr>";
        }

        private static string BuildFooterXml(ProofingAuditReportModel model)
        {
            string leftText = UiLocalizer.Pick(
                "Myanmar Official Spell Checker • Htet Yar Zar (Kyaukse University)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း • ထက်ရာဇာ (ကျောက်ဆည်တက္ကသိုလ်)");
            string pageLbl = UiLocalizer.Pick("Page ", "စာမျက်နှာ ");
            string ofLbl = UiLocalizer.Pick(" of ", " / ");

            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                   "<w:p>" +
                   "<w:pPr>" +
                   "<w:pBdr><w:top w:val=\"single\" w:sz=\"4\" w:space=\"4\" w:color=\"CBD5E1\"/></w:pBdr>" +
                   $"<w:tabs><w:tab w:val=\"right\" w:pos=\"{ContentWidthTwips}\"/></w:tabs>" +
                   "<w:spacing w:before=\"40\" w:after=\"0\"/>" +
                   "</w:pPr>" +
                   BuildRun(leftText, 16, "64748B", bold: false) +
                   "<w:r><w:tab/></w:r>" +
                   BuildRun(pageLbl, 16, "334155", bold: true) +
                   "<w:fldSimple w:instr=\" PAGE \"><w:r><w:rPr><w:noProof/><w:color w:val=\"334155\"/><w:sz w:val=\"16\"/><w:szCs w:val=\"16\"/></w:rPr><w:t>1</w:t></w:r></w:fldSimple>" +
                   BuildRun(ofLbl, 16, "334155", bold: false) +
                   "<w:fldSimple w:instr=\" NUMPAGES \"><w:r><w:rPr><w:noProof/><w:color w:val=\"334155\"/><w:sz w:val=\"16\"/><w:szCs w:val=\"16\"/></w:rPr><w:t>1</w:t></w:r></w:fldSimple>" +
                   "</w:p>" +
                   "</w:ftr>";
        }

        private static string BuildRun(string? text, int halfPtSize, string hexColor, bool bold = false, bool italic = false, bool underline = false)
        {
            var sb = new StringBuilder(160);
            sb.Append("<w:r><w:rPr>");
            sb.Append("<w:rFonts w:ascii=\"Pyidaungsu\" w:hAnsi=\"Pyidaungsu\" w:eastAsia=\"Pyidaungsu\" w:cs=\"Pyidaungsu\"/>");
            if (bold) sb.Append("<w:b/><w:bCs/>");
            if (italic) sb.Append("<w:i/><w:iCs/>");
            sb.Append("<w:noProof/>");
            sb.Append("<w:color w:val=\"").Append(hexColor).Append("\"/>");
            sb.Append("<w:sz w:val=\"").Append(halfPtSize.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
            sb.Append("<w:szCs w:val=\"").Append(halfPtSize.ToString(CultureInfo.InvariantCulture)).Append("\"/>");
            if (underline) sb.Append("<w:u w:val=\"single\"/>");
            sb.Append("</w:rPr><w:t xml:space=\"preserve\">").Append(E(text)).Append("</w:t></w:r>");
            return sb.ToString();
        }

        private static void AppendMarkedContextParagraph(StringBuilder sb, string? markedContext, bool isConfirmedWrong, int halfPtSize)
        {
            string raw = markedContext ?? string.Empty;
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"28\" w:line=\"256\" w:lineRule=\"auto\"/></w:pPr>");
            int open = raw.IndexOf('〖');
            int close = open >= 0 ? raw.IndexOf('〗', open + 1) : -1;
            if (open < 0 || close <= open)
            {
                sb.Append(BuildRun(raw, halfPtSize, "1E293B", bold: false));
            }
            else
            {
                string before = raw.Substring(0, open);
                string target = raw.Substring(open + 1, close - open - 1);
                string after = raw.Substring(close + 1);
                string hiColor = isConfirmedWrong ? "991B1B" : "854D0E";

                if (before.Length > 0)
                    sb.Append(BuildRun(before, halfPtSize, "334155", bold: false));
                sb.Append(BuildRun($"[{target}]", halfPtSize, hiColor, bold: true, underline: true));
                if (after.Length > 0)
                    sb.Append(BuildRun(after, halfPtSize, "334155", bold: false));
            }
            sb.Append("</w:p>");
        }

        private static void AppendSectionHeading(StringBuilder sb, string title, string accentHex)
        {
            sb.Append("<w:p><w:pPr>");
            sb.Append("<w:keepNext/><w:keepLines/>");
            sb.Append("<w:pBdr><w:bottom w:val=\"single\" w:sz=\"10\" w:space=\"3\" w:color=\"").Append(accentHex).Append("\"/></w:pBdr>");
            sb.Append("<w:spacing w:before=\"180\" w:after=\"80\"/>");
            sb.Append("</w:pPr>");
            sb.Append(BuildRun(title, 22, accentHex, bold: true));
            sb.Append("</w:p>");
        }

        private static void AppendTableStart(StringBuilder sb, int[] colWidths, int cellPadVertTwips, int cellPadHorizTwips)
        {
            int totalW = colWidths.Sum();
            sb.Append("<w:tbl><w:tblPr>");
            sb.Append($"<w:tblW w:w=\"{totalW}\" w:type=\"dxa\"/>");
            sb.Append("<w:tblBorders>");
            sb.Append("<w:top w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:left w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:right w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"E2E8F0\"/>");
            sb.Append("</w:tblBorders>");
            sb.Append("<w:tblCellMar>");
            sb.Append($"<w:top w:w=\"{cellPadVertTwips}\" w:type=\"dxa\"/>");
            sb.Append($"<w:left w:w=\"{cellPadHorizTwips}\" w:type=\"dxa\"/>");
            sb.Append($"<w:bottom w:w=\"{cellPadVertTwips}\" w:type=\"dxa\"/>");
            sb.Append($"<w:right w:w=\"{cellPadHorizTwips}\" w:type=\"dxa\"/>");
            sb.Append("</w:tblCellMar>");
            sb.Append("<w:tblLayout w:type=\"fixed\"/>");
            sb.Append("</w:tblPr><w:tblGrid>");
            for (int i = 0; i < colWidths.Length; i++)
            {
                sb.Append($"<w:gridCol w:w=\"{colWidths[i]}\"/>");
            }
            sb.Append("</w:tblGrid>");
        }

        private static void AppendHeaderRow(StringBuilder sb, int[] colWidths, string[] headers, string fillHex, string textHex, int halfPtSize)
        {
            sb.Append("<w:tr><w:trPr><w:cantSplit/><w:tblHeader/></w:trPr>");
            for (int i = 0; i < colWidths.Length; i++)
            {
                sb.Append("<w:tc><w:tcPr>");
                sb.Append($"<w:tcW w:w=\"{colWidths[i]}\" w:type=\"dxa\"/>");
                sb.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fillHex}\"/>");
                sb.Append("<w:vAlign w:val=\"center\"/>");
                sb.Append("</w:tcPr>");
                string align = i == 0 ? "center" : "left";
                sb.Append($"<w:p><w:pPr><w:spacing w:before=\"12\" w:after=\"12\"/><w:jc w:val=\"{align}\"/></w:pPr>");
                sb.Append(BuildRun(headers[i], halfPtSize, textHex, bold: true));
                sb.Append("</w:p></w:tc>");
            }
            sb.Append("</w:tr>");
        }

        private static void AppendSimpleCell(
            StringBuilder sb,
            int widthTwips,
            string? text,
            int halfPtSize,
            string textHex,
            bool bold = false,
            string align = "left",
            string? fillHex = null)
        {
            sb.Append("<w:tc><w:tcPr>");
            sb.Append($"<w:tcW w:w=\"{widthTwips}\" w:type=\"dxa\"/>");
            if (!string.IsNullOrEmpty(fillHex))
            {
                sb.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fillHex}\"/>");
            }
            sb.Append("<w:vAlign w:val=\"top\"/>");
            sb.Append("</w:tcPr>");
            sb.Append($"<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"256\" w:lineRule=\"auto\"/><w:jc w:val=\"{align}\"/></w:pPr>");
            sb.Append(BuildRun(string.IsNullOrWhiteSpace(text) ? "—" : text, halfPtSize, textHex, bold: bold));
            sb.Append("</w:p></w:tc>");
        }

        private static string BuildDocumentXml(ProofingAuditReportModel model, int compactRowCap)
        {
            int totalUnresolved = model.ConfirmedWrongItems.Count + model.SuspiciousReviewItems.Count;
            bool isCompactMode = totalUnresolved > compactRowCap;
            int bodyFontHalfPt = isCompactMode ? 18 : 19; // 9.0 pt vs 9.5 pt
            int subFontHalfPt = isCompactMode ? 16 : 17;  // 8.0 pt vs 8.5 pt
            int padV = isCompactMode ? 52 : 76;
            int padH = isCompactMode ? 80 : 100;

            var sb = new StringBuilder(65536);
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" ");
            sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<w:body>");

            // 0. Top Official Report Title Banner Box (Print-friendly left-accented slate card, low toner usage!)
            sb.Append("<w:tbl><w:tblPr>");
            sb.Append($"<w:tblW w:w=\"{ContentWidthTwips}\" w:type=\"dxa\"/>");
            sb.Append("<w:tblBorders>");
            sb.Append("<w:top w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:left w:val=\"single\" w:sz=\"24\" w:space=\"0\" w:color=\"102A48\"/>");
            sb.Append("<w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:right w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("</w:tblBorders>");
            sb.Append("<w:tblCellMar><w:top w:w=\"110\" w:type=\"dxa\"/><w:left w:w=\"160\" w:type=\"dxa\"/><w:bottom w:w=\"110\" w:type=\"dxa\"/><w:right w:w=\"140\" w:type=\"dxa\"/></w:tblCellMar>");
            sb.Append("<w:tblLayout w:type=\"fixed\"/>");
            sb.Append($"</w:tblPr><w:tblGrid><w:gridCol w:w=\"{ContentWidthTwips}\"/></w:tblGrid>");
            sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr><w:tc><w:tcPr>");
            sb.Append($"<w:tcW w:w=\"{ContentWidthTwips}\" w:type=\"dxa\"/>");
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F8FAFC\"/>");
            sb.Append("</w:tcPr>");
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"30\"/></w:pPr>");
            sb.Append(BuildRun(
                UiLocalizer.Pick(
                    "MYANMAR SPELLING, GRAMMAR & ORTHOGRAPHY AUDIT REPORT (A4)",
                    "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံနှင့် သဒ္ဒါ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (A4)"),
                26, "102A48", bold: true));
            sb.Append("</w:p>");
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"20\"/></w:pPr>");
            sb.Append(BuildRun($"{UiLocalizer.Pick("Source File:", "စစ်ဆေးသည့်ဖိုင် -")} {model.DocumentTitle} ({model.HostAppLabel})   •   {model.StyleModeLabel}", 19, "1E293B", bold: true));
            sb.Append("</w:p>");
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/></w:pPr>");
            sb.Append(BuildRun($"{UiLocalizer.Pick("Audit Date:", "စစ်ဆေးချိန် -")} {model.OfficialDateHeader}   •   {UiLocalizer.Pick("Words / Syllables:", "စကားလုံး / ဝဏ္ဏ -")} {model.TotalWords:N0} / {model.MyanmarSyllables:N0}", 17, "475569", bold: false));
            sb.Append("</w:p></w:tc></w:tr></w:tbl>");

            // ================= SECTION 1: EXECUTIVE SCORECARD & PER-SHEET / SLIDE BREAKDOWN =================
            AppendSectionHeading(
                sb,
                UiLocalizer.Pick(
                    "1. Executive Quality Scorecard & Sheet / Slide Breakdown",
                    "၁။ အရည်အသွေး အနှစ်ချုပ်ရမှတ် နှင့် Sheet / ဆလိုက်အလိုက် စစ်ဆေးချက်"),
                "102A48");

            // 4-Card KPI Row (2580 + 2580 + 2580 + 2578 = 10,318 twips)
            int[] kpiCols = { 2580, 2580, 2580, 2578 };
            AppendTableStart(sb, kpiCols, 85, 100);
            sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");

            AppendKpiCardCell(
                sb, kpiCols[0], "ECFDF5", "106C38",
                UiLocalizer.Pick("CLEAN ACCURACY & GRADE", "အမှားကင်းစင်မှုနှင့် အဆင့်"),
                $"{model.CleanAccuracyPercent:0.##}% ({model.GradeCode})",
                $"{model.OverallScore}% • {model.LocalizedGradeLabel}");

            AppendKpiCardCell(
                sb, kpiCols[1], "FEF2F2", "991B1B",
                UiLocalizer.Pick("CONFIRMED WRONG", "သေချာသော အမှားစစ်စစ်"),
                $"{model.ConfirmedWrongCount:N0} ({model.ConfirmedWrongRatePercent:0.##}%)",
                $"Spell: {model.ConfirmedSpellingCount} | Uni: {model.ConfirmedEncodingAndLookalikeCount} | Style: {model.ConfirmedGrammarAndStyleCount}");

            AppendKpiCardCell(
                sb, kpiCols[2], "FEFCE8", "854D0E",
                UiLocalizer.Pick("SUSPICIOUS / REVIEW", "သံသယ / လူစိစစ်ရန်"),
                $"{model.SuspiciousReviewCount:N0} ({model.SuspiciousRatePercent:0.##}%)",
                $"Names: {model.SuspiciousNameOrOovCount} | Sense: {model.SuspiciousContextMeaningCount} | Consist: {model.ConsistencyWarningCount}");

            AppendKpiCardCell(
                sb, kpiCols[3], "F0F9FF", "0369A1",
                UiLocalizer.Pick("FIXED IN SESSION", "ယခုပြင်ဆင်ပြီး အရေအတွက်"),
                $"{model.SessionFixedCount:N0}",
                $"Unicode: {model.UnicodePurityPercent:0.#}% | Formal: {model.FormalRegisterPercent:0.#}%");

            sb.Append("</w:tr></w:tbl>");

            // Per-Sheet / Per-Slide / Per-Page Breakdown Table
            if (model.SectionBreakdowns != null && model.SectionBreakdowns.Count > 0)
            {
                sb.Append("<w:p><w:pPr><w:keepNext/><w:spacing w:before=\"90\" w:after=\"30\"/></w:pPr>");
                sb.Append(BuildRun(
                    UiLocalizer.Pick(
                        $"Coverage Across All Sheets / Slides / Pages ({model.SectionBreakdowns.Count} audited):",
                        $"ပါဝင်သော Sheet / ဆလိုက် / စာမျက်နှာ အားလုံးအလိုက် အနှစ်ချုပ် ({model.SectionBreakdowns.Count} ခု စစ်ဆေးပြီး) -"),
                    18, "334155", bold: true));
                sb.Append("</w:p>");

                int[] secCols = { 620, 4138, 1750, 1750, 2060 };
                AppendTableStart(sb, secCols, 55, 90);
                AppendHeaderRow(
                    sb,
                    secCols,
                    new[]
                    {
                        "#",
                        UiLocalizer.Pick("Sheet / Slide / Page", "Sheet / ဆလိုက် / စာမျက်နှာ"),
                        UiLocalizer.Pick("Confirmed Wrong", "သေချာအမှား"),
                        UiLocalizer.Pick("Review Needed", "စိစစ်ရန်"),
                        UiLocalizer.Pick("Status", "အခြေအနေ")
                    },
                    "EEF3FA", "102A48", subFontHalfPt);

                int maxSecRows = Math.Min(model.SectionBreakdowns.Count, 80);
                for (int s = 0; s < maxSecRows; s++)
                {
                    var sec = model.SectionBreakdowns[s];
                    string? zebra = (s % 2 == 1) ? "FAFCFF" : null;
                    string statusTxt = sec.TotalIssues == 0
                        ? UiLocalizer.Pick("✓ Clean (0)", "✓ အမှားကင်း (0)")
                        : UiLocalizer.Pick($"⚠ {sec.TotalIssues} finding(s)", $"⚠ တွေ့ရှိချက် {sec.TotalIssues} ခု");
                    string statusHex = sec.ConfirmedWrongCount > 0 ? "991B1B" : (sec.SuspiciousReviewCount > 0 ? "854D0E" : "106C38");

                    sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
                    AppendSimpleCell(sb, secCols[0], (s + 1).ToString(CultureInfo.InvariantCulture), subFontHalfPt, "475569", bold: true, align: "center", fillHex: zebra);
                    AppendSimpleCell(sb, secCols[1], sec.SectionName, subFontHalfPt, "1E293B", bold: true, align: "left", fillHex: zebra);
                    AppendSimpleCell(sb, secCols[2], sec.ConfirmedWrongCount.ToString(CultureInfo.InvariantCulture), subFontHalfPt, sec.ConfirmedWrongCount > 0 ? "991B1B" : "64748B", bold: sec.ConfirmedWrongCount > 0, align: "center", fillHex: zebra);
                    AppendSimpleCell(sb, secCols[3], sec.SuspiciousReviewCount.ToString(CultureInfo.InvariantCulture), subFontHalfPt, sec.SuspiciousReviewCount > 0 ? "854D0E" : "64748B", bold: sec.SuspiciousReviewCount > 0, align: "center", fillHex: zebra);
                    AppendSimpleCell(sb, secCols[4], statusTxt, subFontHalfPt, statusHex, bold: true, align: "left", fillHex: zebra);
                    sb.Append("</w:tr>");
                }
                sb.Append("</w:tbl>");
            }

            // Smart Paper-Saver Banner when > compactRowCap
            if (isCompactMode)
            {
                sb.Append("<w:p><w:pPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FEF9C3\"/><w:spacing w:before=\"110\" w:after=\"50\"/></w:pPr>");
                sb.Append(BuildRun(
                    UiLocalizer.Pick(
                        $"★ Smart A4 Paper-Saver Active ({totalUnresolved:N0} total occurrences): 100% of unique findings ({model.AllRepeatedGroups.Count:N0} distinct items) are consolidated with ×Counts in Section 2 so zero unique mistakes are missed, and the first {compactRowCap:N0} individual occurrences are printed in Sections 3–4 (export Excel .xlsx or CSV for all {totalUnresolved:N0} raw rows).",
                        $"★ A4 စာရွက်ချွေတာရေးစနစ် အသက်ဝင်နေသည် (စုစုပေါင်း {totalUnresolved:N0} ကြိမ်) — မတူညီသော အမှားအမျိုးအစား ({model.AllRepeatedGroups.Count:N0}) မျိုးလုံးကို အပိုင်း (၂) တွင် အကြိမ်ရေ (×Count) ဖြင့် ၁၀၀% ဖော်ပြထားပြီး၊ အပိုင်း (၃-၄) တွင် ပထမဆုံး ({compactRowCap:N0}) ချက်ကို စာရွက်မဖောင်းပွစေရန် စုစည်းပုံနှိပ်ထားပါသည်။"),
                    17, "854D0E", bold: true));
                sb.Append("</w:p>");
            }

            // ================= SECTION 2: TOP / ALL REPEATED FINDINGS SUMMARY (×COUNT) =================
            var groupsToRender = isCompactMode && model.AllRepeatedGroups.Count > 0
                ? model.AllRepeatedGroups
                : model.TopRepeatedGroups;

            AppendSectionHeading(
                sb,
                isCompactMode
                    ? UiLocalizer.Pick(
                        $"2. Consolidated Unique Findings Summary ({groupsToRender.Count:N0} Unique Items Across {totalUnresolved:N0} Occurrences)",
                        $"၂။ ထပ်ခါတလဲလဲနှင့် မတူညီသော တွေ့ရှိချက်အားလုံး အနှစ်ချုပ် ({groupsToRender.Count:N0} မျိုး / စုစုပေါင်း {totalUnresolved:N0} ကြိမ်)")
                    : UiLocalizer.Pick(
                        $"2. Top Repeated Findings Summary ({groupsToRender.Count:N0})",
                        $"၂။ ထပ်ခါတလဲလဲ အများဆုံးတွေ့ရှိရသော စာလုံးများ အနှစ်ချုပ် ({groupsToRender.Count:N0})"),
                "125CB2");

            if (groupsToRender.Count == 0)
            {
                AppendCleanBadgeParagraph(sb, UiLocalizer.Pick("✓ 100% Clean — zero unresolved spelling or grammar findings!", "✓ အမှားကင်းစင်ပါသည် — ပြင်ဆင်ရန် စာလုံးပေါင်းနှင့် သဒ္ဒါအမှား လုံးဝမရှိပါ။"));
            }
            else
            {
                // 5 A4 Portrait Columns: ☐ # (680) | Status (1550) | Found [×Count] (2350) | Correct Fix (2150) | Sample Locations & Reason (3588) = 10,318
                int[] repCols = { 680, 1550, 2350, 2150, 3588 };
                AppendTableStart(sb, repCols, padV, padH);
                AppendHeaderRow(
                    sb,
                    repCols,
                    new[]
                    {
                        "☐ #",
                        UiLocalizer.Pick("Status", "အမျိုးအစား"),
                        UiLocalizer.Pick("Found (×Count)", "တွေ့ရှိစာသား (အကြိမ်)"),
                        UiLocalizer.Pick("How It Should Be", "အမှန်ရေးရန်"),
                        UiLocalizer.Pick("Locations & Rule Note", "တည်နေရာနှင့် မှတ်ချက်")
                    },
                    "EEF3FA", "102A48", bodyFontHalfPt);

                for (int i = 0; i < groupsToRender.Count; i++)
                {
                    var g = groupsToRender[i];
                    string? zebra = (i % 2 == 1) ? "FAFCFF" : null;
                    string statusColor = g.IsConfirmedWrong ? "991B1B" : "854D0E";
                    string foundLabel = g.Count > 1 ? $"{g.VisualOriginal}  [×{g.Count}]" : g.VisualOriginal;

                    sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
                    AppendSimpleCell(sb, repCols[0], $"☐ {i + 1}", subFontHalfPt, "334155", bold: true, align: "center", fillHex: zebra);
                    AppendSimpleCell(sb, repCols[1], g.StatusBadge, subFontHalfPt, statusColor, bold: true, align: "left", fillHex: zebra);
                    AppendSimpleCell(sb, repCols[2], foundLabel, bodyFontHalfPt, statusColor, bold: true, align: "left", fillHex: zebra);
                    AppendSimpleCell(sb, repCols[3], g.RecommendedFix, bodyFontHalfPt, "106C38", bold: true, align: "left", fillHex: zebra);

                    // Stacked Locations + Primary Reason
                    sb.Append("<w:tc><w:tcPr>");
                    sb.Append($"<w:tcW w:w=\"{repCols[4]}\" w:type=\"dxa\"/>");
                    if (zebra != null) sb.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{zebra}\"/>");
                    sb.Append("<w:vAlign w:val=\"top\"/></w:tcPr>");
                    sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"20\" w:line=\"248\" w:lineRule=\"auto\"/></w:pPr>");
                    sb.Append(BuildRun(g.SampleLocations, subFontHalfPt, "1E293B", bold: true));
                    sb.Append("</w:p>");
                    if (!string.IsNullOrWhiteSpace(g.PrimaryReason))
                    {
                        sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"244\" w:lineRule=\"auto\"/></w:pPr>");
                        sb.Append(BuildRun($"• {g.PrimaryReason}", subFontHalfPt, "475569", bold: false));
                        sb.Append("</w:p>");
                    }
                    sb.Append("</w:tc></w:tr>");
                }
                sb.Append("</w:tbl>");
            }

            // Proportional row budget across Section 3 & Section 4 when in Smart Paper-Saver Mode
            int wrongCap = model.ConfirmedWrongItems.Count;
            int suspCap = model.SuspiciousReviewItems.Count;
            if (isCompactMode)
            {
                if (wrongCap + suspCap > compactRowCap)
                {
                    wrongCap = Math.Min(model.ConfirmedWrongItems.Count, Math.Max(1, (int)Math.Round(compactRowCap * 0.72)));
                    suspCap = Math.Min(model.SuspiciousReviewItems.Count, Math.Max(0, compactRowCap - wrongCap));
                    if (wrongCap + suspCap < compactRowCap && model.ConfirmedWrongItems.Count > wrongCap)
                    {
                        wrongCap = Math.Min(model.ConfirmedWrongItems.Count, compactRowCap - suspCap);
                    }
                }
            }

            // ================= SECTION 3: CONFIRMED WRONG — HOW IT SHOULD BE =================
            string sec3Title = wrongCap < model.ConfirmedWrongItems.Count
                ? UiLocalizer.Pick(
                    $"3. Confirmed Wrong — How It Should Be (Showing First {wrongCap:N0} of {model.ConfirmedWrongItems.Count:N0})",
                    $"၃။ သေချာသော အမှားစစ်စစ်များနှင့် အမှန်ရေးရန် (စုစုပေါင်း {model.ConfirmedWrongItems.Count:N0} မှ ပထမ {wrongCap:N0} ချက်)")
                : UiLocalizer.Pick(
                    $"3. Confirmed Wrong — How It Should Be ({model.ConfirmedWrongItems.Count:N0})",
                    $"၃။ သေချာသော အမှားစစ်စစ်များနှင့် အမှန်ရေးရန် ({model.ConfirmedWrongItems.Count:N0})");

            AppendSectionHeading(sb, sec3Title, "991B1B");
            AppendDetailedFindingsTable(
                sb,
                model.ConfirmedWrongItems,
                wrongCap,
                isConfirmedWrongTable: true,
                bodyFontHalfPt: bodyFontHalfPt,
                subFontHalfPt: subFontHalfPt,
                padV: padV,
                padH: padH);

            // ================= SECTION 4: SUSPICIOUS / HUMAN REVIEW NEEDED =================
            string sec4Title = suspCap < model.SuspiciousReviewItems.Count
                ? UiLocalizer.Pick(
                    $"4. Suspicious / Human Review Needed (Showing First {suspCap:N0} of {model.SuspiciousReviewItems.Count:N0})",
                    $"၄။ သံသယရှိ / လူကိုယ်တိုင် စိစစ်အတည်ပြုရန် ({model.SuspiciousReviewItems.Count:N0} မှ ပထမ {suspCap:N0} ချက်)")
                : UiLocalizer.Pick(
                    $"4. Suspicious / Human Review Needed ({model.SuspiciousReviewItems.Count:N0})",
                    $"၄။ သံသယရှိ / လူကိုယ်တိုင် စိစစ်အတည်ပြုရန် ({model.SuspiciousReviewItems.Count:N0})");

            AppendSectionHeading(sb, sec4Title, "854D0E");
            AppendDetailedFindingsTable(
                sb,
                model.SuspiciousReviewItems,
                suspCap,
                isConfirmedWrongTable: false,
                bodyFontHalfPt: bodyFontHalfPt,
                subFontHalfPt: subFontHalfPt,
                padV: padV,
                padH: padH);

            // ================= SECTION 5: ALREADY FIXED IN THIS SESSION (OPTIONAL) =================
            if (model.SessionFixedRecords.Count > 0)
            {
                int fixedTake = Math.Min(model.SessionFixedRecords.Count, 150);
                AppendSectionHeading(
                    sb,
                    UiLocalizer.Pick(
                        $"5. Already Fixed in This Session ({model.SessionFixedRecords.Count:N0})",
                        $"၅။ ယခုအကြိမ် ပြင်ဆင်ပြီးစီးခဲ့သော မှတ်တမ်း ({model.SessionFixedRecords.Count:N0})"),
                    "106C38");

                int[] fixCols = { 680, 2500, 2900, 2900, 1338 };
                AppendTableStart(sb, fixCols, padV, padH);
                AppendHeaderRow(
                    sb,
                    fixCols,
                    new[]
                    {
                        "#",
                        UiLocalizer.Pick("Location", "နေရာ"),
                        UiLocalizer.Pick("Original Error", "မူလအမှား"),
                        UiLocalizer.Pick("Applied Fix", "ပြင်ဆင်ပြီးအမှန်"),
                        UiLocalizer.Pick("Time", "အချိန်")
                    },
                    "ECFDF5", "106C38", subFontHalfPt);

                for (int i = 0; i < fixedTake; i++)
                {
                    var f = model.SessionFixedRecords[i];
                    string? zebra = (i % 2 == 1) ? "F8FAFC" : null;
                    sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
                    AppendSimpleCell(sb, fixCols[0], (i + 1).ToString(CultureInfo.InvariantCulture), subFontHalfPt, "475569", bold: true, align: "center", fillHex: zebra);
                    AppendSimpleCell(sb, fixCols[1], f.LocationLabel, subFontHalfPt, "1E293B", bold: false, align: "left", fillHex: zebra);
                    AppendSimpleCell(sb, fixCols[2], f.VisualOriginalText, bodyFontHalfPt, "991B1B", bold: true, align: "left", fillHex: zebra);
                    AppendSimpleCell(sb, fixCols[3], $"✓ {f.AppliedReplacement}", bodyFontHalfPt, "106C38", bold: true, align: "left", fillHex: zebra);
                    AppendSimpleCell(sb, fixCols[4], f.AppliedAtLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture), subFontHalfPt, "64748B", bold: false, align: "center", fillHex: zebra);
                    sb.Append("</w:tr>");
                }
                sb.Append("</w:tbl>");
            }

            // ================= OFFICIAL A4 PRINT SIGN-OFF & APPROVAL BOX =================
            sb.Append("<w:p><w:pPr><w:keepNext/><w:spacing w:before=\"220\" w:after=\"50\"/></w:pPr>");
            sb.Append(BuildRun(
                UiLocalizer.Pick(
                    "Official Proofreading Review & Sign-Off (စစ်ဆေးအတည်ပြုချက် လက်မှတ်ရေးထိုးရန်)",
                    "စာလုံးပေါင်းသတ်ပုံ စစ်ဆေးအတည်ပြုချက် လက်မှတ်ရေးထိုးရန် (Review & Sign-Off)"),
                18, "334155", bold: true));
            sb.Append("</w:p>");

            int[] signCols = { 5159, 5159 };
            AppendTableStart(sb, signCols, 90, 120);
            sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
            AppendSignOffCell(
                sb, signCols[0],
                UiLocalizer.Pick("Prepared / Proofread By (စစ်ဆေးသူ):", "စစ်ဆေးပြုစုသူ (Proofread By) -"),
                UiLocalizer.Pick("Name & Title: ____________________________", "အမည်နှင့် ရာထူး - ____________________________"),
                UiLocalizer.Pick("Signature & Date: ________________________", "လက်မှတ်နှင့် ရက်စွဲ - ________________________"));
            AppendSignOffCell(
                sb, signCols[1],
                UiLocalizer.Pick("Reviewed / Approved By (အတည်ပြုသူ):", "စိစစ်အတည်ပြုသူ (Approved By) -"),
                UiLocalizer.Pick("Name & Title: ____________________________", "အမည်နှင့် ရာထူး - ____________________________"),
                UiLocalizer.Pick("Signature & Date: ________________________", "လက်မှတ်နှင့် ရက်စွဲ - ________________________"));
            sb.Append("</w:tr></w:tbl>");

            // Mandatory ISO A4 Portrait Section Properties (210 mm x 297 mm = 11906 x 16838 dxa)
            sb.Append("<w:sectPr>");
            sb.Append("<w:headerReference w:type=\"default\" r:id=\"rId3\"/>");
            sb.Append("<w:footerReference w:type=\"default\" r:id=\"rId4\"/>");
            sb.Append($"<w:pgSz w:w=\"{IsoA4PortraitWidthTwips}\" w:h=\"{IsoA4PortraitHeightTwips}\"/>");
            sb.Append($"<w:pgMar w:top=\"{MarginTopTwips}\" w:right=\"{MarginSideTwips}\" w:bottom=\"{MarginBottomTwips}\" w:left=\"{MarginSideTwips}\" w:header=\"454\" w:footer=\"454\" w:gutter=\"0\"/>");
            sb.Append("</w:sectPr>");

            sb.Append("</w:body></w:document>");
            return sb.ToString();
        }

        private static void AppendDetailedFindingsTable(
            StringBuilder sb,
            IReadOnlyList<ProofingAuditItemRow> items,
            int takeCount,
            bool isConfirmedWrongTable,
            int bodyFontHalfPt,
            int subFontHalfPt,
            int padV,
            int padH)
        {
            if (items == null || items.Count == 0 || takeCount <= 0)
            {
                string cleanMsg = isConfirmedWrongTable
                    ? UiLocalizer.Pick("✓ 0 confirmed spelling, Unicode, or grammar errors remaining.", "✓ သေချာသော စာလုံးပေါင်းနှင့် သဒ္ဒါအမှား လုံးဝမရှိပါ။")
                    : UiLocalizer.Pick("✓ 0 suspicious or contextual meaning items requiring manual review.", "✓ သံသယရှိ လူစိစစ်ရန် စာလုံး မရှိပါ။");
                AppendCleanBadgeParagraph(sb, cleanMsg);
                return;
            }

            // 5 Crisp A4 Portrait Columns (Total = 10,318 twips = 182 mm):
            // 1. ☐ # (680) | 2. Location (1680) | 3. Wrong/Suspicious (1920) | 4. Correct Fix (1920) | 5. Stacked Context + MLC Rule (4118)
            int[] cols = { 680, 1680, 1920, 1920, 4118 };
            string hdrFill = isConfirmedWrongTable ? "FEF2F2" : "FEFCE8";
            string hdrText = isConfirmedWrongTable ? "991B1B" : "854D0E";
            string origTextHex = isConfirmedWrongTable ? "991B1B" : "854D0E";

            AppendTableStart(sb, cols, padV, padH);
            AppendHeaderRow(
                sb,
                cols,
                new[]
                {
                    "☐ #",
                    UiLocalizer.Pick("Location", "တည်နေရာ"),
                    isConfirmedWrongTable
                        ? UiLocalizer.Pick("Wrong Text", "မှားနေသောစာသား")
                        : UiLocalizer.Pick("Suspicious Text", "သံသယစာသား"),
                    isConfirmedWrongTable
                        ? UiLocalizer.Pick("How It Should Be", "အမှန်ရေးရန်")
                        : UiLocalizer.Pick("Suggested (If Typo)", "အကြံပြုအမှန်"),
                    UiLocalizer.Pick("Sentence Context & MLC Rule", "ဝါကျရှေ့နောက်စကား နှင့် စည်းကမ်းချက်")
                },
                hdrFill, hdrText, bodyFontHalfPt);

            int count = Math.Min(items.Count, takeCount);
            for (int i = 0; i < count; i++)
            {
                var r = items[i];
                string? zebra = (i % 2 == 1) ? "FAFCFF" : null;

                sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");

                // Col 0: Pen-Check ☐ + Index
                AppendSimpleCell(sb, cols[0], $"☐ {r.Index}", subFontHalfPt, "334155", bold: true, align: "center", fillHex: zebra);

                // Col 1: Location + Category Badge stacked
                sb.Append("<w:tc><w:tcPr>");
                sb.Append($"<w:tcW w:w=\"{cols[1]}\" w:type=\"dxa\"/>");
                if (zebra != null) sb.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{zebra}\"/>");
                sb.Append("<w:vAlign w:val=\"top\"/></w:tcPr>");
                sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"16\" w:line=\"244\" w:lineRule=\"auto\"/></w:pPr>");
                sb.Append(BuildRun(r.LocationLabel, subFontHalfPt, "1E293B", bold: true));
                sb.Append("</w:p>");
                sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"236\" w:lineRule=\"auto\"/></w:pPr>");
                sb.Append(BuildRun($"[{r.CategoryLabel}]", subFontHalfPt - 1, origTextHex, bold: false));
                sb.Append("</w:p></w:tc>");

                // Col 2: Wrong / Suspicious Text
                AppendSimpleCell(sb, cols[2], r.VisualOriginal, bodyFontHalfPt, origTextHex, bold: true, align: "left", fillHex: zebra);

                // Col 3: Recommended Fix
                AppendSimpleCell(sb, cols[3], r.HowItShouldBeDisplay, bodyFontHalfPt, "106C38", bold: true, align: "left", fillHex: zebra);

                // Col 4: Stacked Context Sentence (with bold underlined [target]) + Concise Rule Note
                sb.Append("<w:tc><w:tcPr>");
                sb.Append($"<w:tcW w:w=\"{cols[4]}\" w:type=\"dxa\"/>");
                if (zebra != null) sb.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{zebra}\"/>");
                sb.Append("<w:vAlign w:val=\"top\"/></w:tcPr>");

                string ctx = !string.IsNullOrWhiteSpace(r.MarkedContextSnippet) ? r.MarkedContextSnippet : r.ContextSnippet;
                if (!string.IsNullOrWhiteSpace(ctx))
                {
                    AppendMarkedContextParagraph(sb, ctx, isConfirmedWrongTable, subFontHalfPt);
                }
                sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"244\" w:lineRule=\"auto\"/></w:pPr>");
                sb.Append(BuildRun($"• {r.Explanation}", subFontHalfPt, "475569", bold: false));
                sb.Append("</w:p></w:tc>");

                sb.Append("</w:tr>");
            }

            sb.Append("</w:tbl>");
        }

        private static void AppendKpiCardCell(
            StringBuilder sb,
            int widthTwips,
            string fillHex,
            string accentHex,
            string label,
            string bigVal,
            string subText)
        {
            sb.Append("<w:tc><w:tcPr>");
            sb.Append($"<w:tcW w:w=\"{widthTwips}\" w:type=\"dxa\"/>");
            sb.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fillHex}\"/>");
            sb.Append("<w:vAlign w:val=\"top\"/></w:tcPr>");
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"18\"/></w:pPr>");
            sb.Append(BuildRun(label, 15, "475569", bold: true));
            sb.Append("</w:p>");
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"18\"/></w:pPr>");
            sb.Append(BuildRun(bigVal, 24, accentHex, bold: true));
            sb.Append("</w:p>");
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"236\" w:lineRule=\"auto\"/></w:pPr>");
            sb.Append(BuildRun(subText, 15, "334155", bold: false));
            sb.Append("</w:p></w:tc>");
        }

        private static void AppendSignOffCell(StringBuilder sb, int widthTwips, string title, string line1, string line2)
        {
            sb.Append("<w:tc><w:tcPr>");
            sb.Append($"<w:tcW w:w=\"{widthTwips}\" w:type=\"dxa\"/>");
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F8FAFC\"/>");
            sb.Append("<w:vAlign w:val=\"top\"/></w:tcPr>");
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"60\"/></w:pPr>");
            sb.Append(BuildRun(title, 17, "102A48", bold: true));
            sb.Append("</w:p>");
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"60\"/></w:pPr>");
            sb.Append(BuildRun(line1, 17, "334155", bold: false));
            sb.Append("</w:p>");
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/></w:pPr>");
            sb.Append(BuildRun(line2, 17, "334155", bold: false));
            sb.Append("</w:p></w:tc>");
        }

        private static void AppendCleanBadgeParagraph(StringBuilder sb, string text)
        {
            sb.Append("<w:p><w:pPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"ECFDF5\"/><w:spacing w:before=\"40\" w:after=\"80\"/></w:pPr>");
            sb.Append(BuildRun($"  {text}  ", 18, "106C38", bold: true));
            sb.Append("</w:p>");
        }
    }
}
