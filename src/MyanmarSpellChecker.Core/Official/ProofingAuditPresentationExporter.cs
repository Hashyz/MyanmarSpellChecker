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
    /// Generates a 100% standards-compliant 16:9 Widescreen Microsoft PowerPoint (.pptx) Executive Proofing Summary Deck
    /// in pure .NET Framework 4.8 (zero external NuGet packages and zero PowerPoint COM dependency).
    /// Works identically from Microsoft Word, Microsoft Excel, and Microsoft PowerPoint:
    /// - Slide 1: Executive Overview &amp; 4-KPI Quality Scorecard (Score/Grade, Clean %, Confirmed Wrong, Review Needed)
    /// - Slide 2: Per-Sheet / Per-Slide / Per-Page Coverage Breakdown (including 0-issue clean sheets/slides)
    /// - Slide 3: Top Priority Repeated Mistakes (Found [xCount] -&gt; Recommended Fix + Sample Locations)
    /// - Slide 4: Key Confirmed Wrong &amp; Review Highlights + Official Proofing Sign-Off Summary
    /// All runs carry noProof="1" and Pyidaungsu font bindings so PowerPoint renders crisp Myanmar script with zero red squiggles.
    /// </summary>
    public static class ProofingAuditPresentationExporter
    {
        public const long SlideWidthEmu = 12192000L;  // 13.333 in (16:9 Widescreen)
        public const long SlideHeightEmu = 6858000L;  // 7.500 in

        public static string SaveExecutivePptxToDisk(
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
            string fileName = $"ProofingDeck_{stem}_{model.GeneratedAtLocal:yyyyMMdd_HHmmss}.pptx";
            string fullPath = Path.Combine(baseDir, fileName);

            byte[] pptxBytes = BuildExecutivePptxBytes(model);
            File.WriteAllBytes(fullPath, pptxBytes);
            return fullPath;
        }

        public static byte[] BuildExecutivePptxBytes(ProofingAuditReportModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteXmlEntry(zip, "[Content_Types].xml", BuildContentTypesXml(4));
                    WriteXmlEntry(zip, "_rels/.rels", BuildRootRelsXml());
                    WriteXmlEntry(zip, "docProps/core.xml", BuildCorePropsXml(model));
                    WriteXmlEntry(zip, "docProps/app.xml", BuildAppPropsXml());
                    WriteXmlEntry(zip, "ppt/presentation.xml", BuildPresentationXml(4));
                    WriteXmlEntry(zip, "ppt/_rels/presentation.xml.rels", BuildPresentationRelsXml(4));
                    WriteXmlEntry(zip, "ppt/presProps.xml", BuildPresPropsXml());
                    WriteXmlEntry(zip, "ppt/viewProps.xml", BuildViewPropsXml());
                    WriteXmlEntry(zip, "ppt/tableStyles.xml", BuildTableStylesXml());
                    WriteXmlEntry(zip, "ppt/theme/theme1.xml", BuildThemeXml());
                    WriteXmlEntry(zip, "ppt/slideMasters/slideMaster1.xml", BuildSlideMasterXml());
                    WriteXmlEntry(zip, "ppt/slideMasters/_rels/slideMaster1.xml.rels", BuildSlideMasterRelsXml());
                    WriteXmlEntry(zip, "ppt/slideLayouts/slideLayout1.xml", BuildSlideLayoutXml());
                    WriteXmlEntry(zip, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", BuildSlideLayoutRelsXml());

                    string slideRels = BuildSlideRelsXml();
                    WriteXmlEntry(zip, "ppt/slides/slide1.xml", BuildSlide1ExecutiveScorecardXml(model));
                    WriteXmlEntry(zip, "ppt/slides/_rels/slide1.xml.rels", slideRels);

                    WriteXmlEntry(zip, "ppt/slides/slide2.xml", BuildSlide2SectionBreakdownXml(model));
                    WriteXmlEntry(zip, "ppt/slides/_rels/slide2.xml.rels", slideRels);

                    WriteXmlEntry(zip, "ppt/slides/slide3.xml", BuildSlide3RepeatedMistakesXml(model));
                    WriteXmlEntry(zip, "ppt/slides/_rels/slide3.xml.rels", slideRels);

                    WriteXmlEntry(zip, "ppt/slides/slide4.xml", BuildSlide4KeyHighlightsXml(model));
                    WriteXmlEntry(zip, "ppt/slides/_rels/slide4.xml.rels", slideRels);
                }

                return ms.ToArray();
            }
        }

        private static void WriteXmlEntry(ZipArchive zip, string entryPath, string xmlContent)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(xmlContent);
            }
        }

        private static string Esc(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value!.Length);
            foreach (char ch in value)
            {
                if (ch == '\t' || ch == '\n' || ch == '\r' ||
                    (ch >= 0x20 && ch <= 0xD7FF) ||
                    (ch >= 0xE000 && ch <= 0xFFFD))
                {
                    sb.Append(ch);
                }
            }
            return SecurityElement.Escape(sb.ToString()) ?? string.Empty;
        }

        private static string Trunc(string? text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            string clean = text!.Replace("\r", " ").Replace("\n", " ").Trim();
            if (clean.Length <= maxChars)
                return clean;
            return clean.Substring(0, Math.Max(1, maxChars - 1)) + "…";
        }

        private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static string BuildContentTypesXml(int slideCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>");
            sb.Append("<Override PartName=\"/ppt/presProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presProps+xml\"/>");
            sb.Append("<Override PartName=\"/ppt/viewProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.viewProps+xml\"/>");
            sb.Append("<Override PartName=\"/ppt/tableStyles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.tableStyles+xml\"/>");
            sb.Append("<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>");
            sb.Append("<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>");
            sb.Append("<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>");
            sb.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            sb.Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>");
            for (int i = 1; i <= slideCount; i++)
            {
                sb.Append($"<Override PartName=\"/ppt/slides/slide{Inv(i)}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");
            }
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string BuildRootRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/>" +
                   "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                   "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
                   "</Relationships>";
        }

        private static string BuildCorePropsXml(ProofingAuditReportModel model)
        {
            string isoNow = model.GeneratedAtLocal.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                   "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                   "xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                   "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                   $"<dc:title>{Esc(model.DocumentTitle)} — Myanmar Proofing Executive Summary</dc:title>" +
                   "<dc:creator>Myanmar Spell Checker Suite — Htet Yar Zar (ထက်ရာဇာ), Kyaukse University</dc:creator>" +
                   $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{isoNow}</dcterms:created>" +
                   $"<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{isoNow}</dcterms:modified>" +
                   "</cp:coreProperties>";
        }

        private static string BuildAppPropsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" " +
                   "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
                   "<Application>Myanmar Spell Checker Office Suite</Application>" +
                   "<PresentationFormat>Widescreen</PresentationFormat>" +
                   "<Slides>4</Slides>" +
                   "<Notes>0</Notes>" +
                   "<HiddenSlides>0</HiddenSlides>" +
                   "<Company>Department of Geology, Kyaukse University</Company>" +
                   "</Properties>";
        }

        private static string BuildPresentationXml(int slideCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<p:presentation xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" ");
            sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" ");
            sb.Append("xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" saveSubsetFonts=\"1\">");
            sb.Append("<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>");
            sb.Append("<p:sldIdLst>");
            for (int i = 1; i <= slideCount; i++)
            {
                sb.Append($"<p:sldId id=\"{Inv(255 + i)}\" r:id=\"rId{Inv(10 + i)}\"/>");
            }
            sb.Append("</p:sldIdLst>");
            sb.Append($"<p:sldSz cx=\"{Inv(SlideWidthEmu)}\" cy=\"{Inv(SlideHeightEmu)}\"/>");
            sb.Append("<p:notesSz cx=\"6858000\" cy=\"9144000\"/>");
            sb.Append("<p:defaultTextStyle><a:defPPr><a:defRPr lang=\"my-MM\" sz=\"1400\" dirty=\"0\" noProof=\"1\">");
            sb.Append("<a:latin typeface=\"Pyidaungsu\"/><a:ea typeface=\"Pyidaungsu\"/><a:cs typeface=\"Pyidaungsu\"/>");
            sb.Append("</a:defRPr></a:defPPr></p:defaultTextStyle>");
            sb.Append("</p:presentation>");
            return sb.ToString();
        }

        private static string BuildPresentationRelsXml(int slideCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            sb.Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/>");
            sb.Append("<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/presProps\" Target=\"presProps.xml\"/>");
            sb.Append("<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/viewProps\" Target=\"viewProps.xml\"/>");
            sb.Append("<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"theme/theme1.xml\"/>");
            sb.Append("<Relationship Id=\"rId5\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableStyles\" Target=\"tableStyles.xml\"/>");
            for (int i = 1; i <= slideCount; i++)
            {
                sb.Append($"<Relationship Id=\"rId{Inv(10 + i)}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide{Inv(i)}.xml\"/>");
            }
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string BuildPresPropsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<p:presentationPr xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                   "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                   "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"/>";
        }

        private static string BuildViewPropsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<p:viewPr xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                   "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                   "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"/>";
        }

        private static string BuildTableStylesXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<a:tblStyleLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" def=\"{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}\"/>";
        }

        private static string BuildThemeXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"MyanmarProofingTheme\">" +
                   "<a:themeElements>" +
                   "<a:clrScheme name=\"MyanmarProofing\">" +
                   "<a:dk1><a:srgbClr val=\"0F172A\"/></a:dk1>" +
                   "<a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1>" +
                   "<a:dk2><a:srgbClr val=\"1E293B\"/></a:dk2>" +
                   "<a:lt2><a:srgbClr val=\"F8FAFC\"/></a:lt2>" +
                   "<a:accent1><a:srgbClr val=\"1E3A8A\"/></a:accent1>" +
                   "<a:accent2><a:srgbClr val=\"991B1B\"/></a:accent2>" +
                   "<a:accent3><a:srgbClr val=\"047857\"/></a:accent3>" +
                   "<a:accent4><a:srgbClr val=\"B45309\"/></a:accent4>" +
                   "<a:accent5><a:srgbClr val=\"4338CA\"/></a:accent5>" +
                   "<a:accent6><a:srgbClr val=\"0369A1\"/></a:accent6>" +
                   "<a:hlink><a:srgbClr val=\"1D4ED8\"/></a:hlink>" +
                   "<a:folHlink><a:srgbClr val=\"6D28D9\"/></a:folHlink>" +
                   "</a:clrScheme>" +
                   "<a:fontScheme name=\"PyidaungsuScheme\">" +
                   "<a:majorFont><a:latin typeface=\"Pyidaungsu\"/><a:ea typeface=\"Pyidaungsu\"/><a:cs typeface=\"Pyidaungsu\"/></a:majorFont>" +
                   "<a:minorFont><a:latin typeface=\"Pyidaungsu\"/><a:ea typeface=\"Pyidaungsu\"/><a:cs typeface=\"Pyidaungsu\"/></a:minorFont>" +
                   "</a:fontScheme>" +
                   "<a:fmtScheme name=\"Office\">" +
                   "<a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst>" +
                   "<a:lnStyleLst><a:ln w=\"9525\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"25400\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln></a:lnStyleLst>" +
                   "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
                   "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:bgFillStyleLst>" +
                   "</a:fmtScheme>" +
                   "</a:themeElements>" +
                   "<a:objectDefaults/>" +
                   "<a:extraClrSchemeLst/>" +
                   "</a:theme>";
        }

        private static string BuildSlideMasterXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<p:sldMaster xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                   "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                   "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
                   "<p:cSld>" +
                   "<p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg>" +
                   "<p:spTree>" +
                   "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
                   "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
                   "</p:spTree>" +
                   "</p:cSld>" +
                   "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
                   "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst>" +
                   "</p:sldMaster>";
        }

        private static string BuildSlideMasterRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>" +
                   "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"../theme/theme1.xml\"/>" +
                   "</Relationships>";
        }

        private static string BuildSlideLayoutXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<p:sldLayout xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                   "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                   "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" type=\"blank\" preserve=\"1\">" +
                   "<p:cSld name=\"Blank\">" +
                   "<p:spTree>" +
                   "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
                   "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
                   "</p:spTree>" +
                   "</p:cSld>" +
                   "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>" +
                   "</p:sldLayout>";
        }

        private static string BuildSlideLayoutRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/>" +
                   "</Relationships>";
        }

        private static string BuildSlideRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>" +
                   "</Relationships>";
        }

        private static string BeginSlideXml(ref int nextShapeId, ProofingAuditReportModel model, string slideSubtitle, int slideIndex, int totalSlides)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<p:sld xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" ");
            sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" ");
            sb.Append("xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">");
            sb.Append("<p:cSld><p:spTree>");
            sb.Append("<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>");
            sb.Append("<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>");

            // Subtle slate background canvas
            sb.Append(BuildRectShape(
                nextShapeId++, "SlideBg", 0, 0, SlideWidthEmu, SlideHeightEmu,
                "F8FAFC", null, "rect", "ctr",
                Array.Empty<(string, int, bool, string, string)>()));

            // Top Royal Navy Header Banner
            sb.Append(BuildRectShape(
                nextShapeId++, "HeaderBanner", 0, 0, SlideWidthEmu, 920000,
                "0F172A", null, "rect", "ctr",
                new[]
                {
                    ($"{Trunc(model.DocumentTitle, 65)} — {slideSubtitle}", 1850, true, "FFFFFF", "l"),
                    ($"{model.HostAppLabel}  •  {model.StyleModeLabel}  •  {Trunc(model.OfficialDateHeader, 88)}", 1050, false, "CBD5E1", "l")
                },
                leftIns: 420000, rightIns: 1600000));

            // Slide Badge Pill on top-right
            sb.Append(BuildRectShape(
                nextShapeId++, "SlidePill", SlideWidthEmu - 1520000, 220000, 1140000, 440000,
                "1E3A8A", "3B82F6", "roundRect", "ctr",
                new[]
                {
                    ($"Slide {slideIndex} / {totalSlides}", 1150, true, "FFFFFF", "ctr")
                },
                leftIns: 60000, rightIns: 60000));

            // Bottom Footer Strip
            sb.Append(BuildRectShape(
                nextShapeId++, "FooterBar", 0, SlideHeightEmu - 360000, SlideWidthEmu, 360000,
                "E2E8F0", null, "rect", "ctr",
                new[]
                {
                    ("Myanmar Spell Checker & Official Orthography Suite (MLC Standard)  •  Created by Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University", 950, false, "334155", "l")
                },
                leftIns: 420000, rightIns: 420000));

            return sb.ToString();
        }

        private static string EndSlideXml()
        {
            return "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";
        }

        private static string BuildRectShape(
            int shapeId,
            string name,
            long x,
            long y,
            long cx,
            long cy,
            string? fillHex,
            string? borderHex,
            string geomPreset,
            string verticalAnchor,
            IReadOnlyList<(string Text, int FontSizeHundredthsPt, bool Bold, string ColorHex, string Align)> paragraphs,
            long leftIns = 140000,
            long rightIns = 140000,
            long topIns = 75000,
            long bottomIns = 75000)
        {
            var sb = new StringBuilder();
            sb.Append("<p:sp>");
            sb.Append($"<p:nvSpPr><p:cNvPr id=\"{Inv(shapeId)}\" name=\"{Esc(name)}\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>");
            sb.Append("<p:spPr>");
            sb.Append($"<a:xfrm><a:off x=\"{Inv(x)}\" y=\"{Inv(y)}\"/><a:ext cx=\"{Inv(cx)}\" cy=\"{Inv(cy)}\"/></a:xfrm>");
            sb.Append($"<a:prstGeom prst=\"{Esc(geomPreset)}\"><a:avLst/></a:prstGeom>");
            if (!string.IsNullOrEmpty(fillHex))
            {
                sb.Append($"<a:solidFill><a:srgbClr val=\"{Esc(fillHex)}\"/></a:solidFill>");
            }
            else
            {
                sb.Append("<a:noFill/>");
            }

            if (!string.IsNullOrEmpty(borderHex))
            {
                sb.Append($"<a:ln w=\"12700\"><a:solidFill><a:srgbClr val=\"{Esc(borderHex)}\"/></a:solidFill></a:ln>");
            }
            else
            {
                sb.Append("<a:ln><a:noFill/></a:ln>");
            }
            sb.Append("</p:spPr>");

            sb.Append("<p:txBody>");
            sb.Append($"<a:bodyPr wrap=\"square\" lIns=\"{Inv(leftIns)}\" tIns=\"{Inv(topIns)}\" rIns=\"{Inv(rightIns)}\" bIns=\"{Inv(bottomIns)}\" anchor=\"{Esc(verticalAnchor)}\"/>");
            sb.Append("<a:lstStyle/>");

            if (paragraphs == null || paragraphs.Count == 0)
            {
                sb.Append("<a:p><a:endParaRPr lang=\"my-MM\" dirty=\"0\" noProof=\"1\"/></a:p>");
            }
            else
            {
                foreach (var p in paragraphs)
                {
                    string algn = string.IsNullOrEmpty(p.Align) ? "l" : p.Align;
                    string boldAttr = p.Bold ? " b=\"1\"" : string.Empty;
                    sb.Append($"<a:p><a:pPr algn=\"{Esc(algn)}\"/>");
                    sb.Append("<a:r>");
                    sb.Append($"<a:rPr lang=\"my-MM\" altLang=\"en-US\" sz=\"{Inv(p.FontSizeHundredthsPt)}\"{boldAttr} dirty=\"0\" noProof=\"1\">");
                    sb.Append($"<a:solidFill><a:srgbClr val=\"{Esc(p.ColorHex)}\"/></a:solidFill>");
                    sb.Append("<a:latin typeface=\"Pyidaungsu\"/><a:ea typeface=\"Pyidaungsu\"/><a:cs typeface=\"Pyidaungsu\"/>");
                    sb.Append("</a:rPr>");
                    sb.Append($"<a:t>{Esc(p.Text)}</a:t>");
                    sb.Append("</a:r></a:p>");
                }
            }

            sb.Append("</p:txBody></p:sp>");
            return sb.ToString();
        }

        private static string BuildSlide1ExecutiveScorecardXml(ProofingAuditReportModel model)
        {
            int id = 2;
            var sb = new StringBuilder();
            sb.Append(BeginSlideXml(ref id, model, UiLocalizer.Pick("Executive Proofing Scorecard", "အမှုဆောင် သတ်ပုံစစ်ဆေးမှု အကျဉ်းချုပ်"), 1, 4));

            // Metadata summary strip
            int sectionCount = Math.Max(1, model.SectionBreakdowns.Count);
            int cleanSections = model.SectionBreakdowns.Count(s => s.TotalIssues == 0);
            sb.Append(BuildRectShape(
                id++, "MetaStrip", 420000, 1100000, SlideWidthEmu - 840000, 560000,
                "FFFFFF", "CBD5E1", "roundRect", "ctr",
                new[]
                {
                    ($"Audited Scope: {sectionCount} Sheet(s)/Slide(s)/Page(s) ({cleanSections} Clean)   •   Words: {model.TotalWords:N0}   •   Myanmar Syllables: {model.MyanmarSyllables:N0}   •   Unicode Purity: {model.UnicodePurityPercent:0.0}%",
                        1200, true, "1E293B", "l")
                },
                leftIns: 200000, rightIns: 200000));

            // 4 KPI Cards
            long cardW = 2720000;
            long gap = 157000;
            long startX = 420000;
            long cardY = 1840000;
            long cardH = 1950000;

            string scoreFill = model.OverallScore >= 90 ? "ECFDF5" : (model.OverallScore >= 75 ? "EFF6FF" : "FEF2F2");
            string scoreBorder = model.OverallScore >= 90 ? "10B981" : (model.OverallScore >= 75 ? "3B82F6" : "EF4444");
            string scoreText = model.OverallScore >= 90 ? "065F46" : (model.OverallScore >= 75 ? "1E3A8A" : "991B1B");

            sb.Append(BuildRectShape(
                id++, "KpiScore", startX, cardY, cardW, cardH,
                scoreFill, scoreBorder, "roundRect", "ctr",
                new[]
                {
                    ("OVERALL QUALITY SCORE", 1050, true, "475569", "ctr"),
                    ($"{model.OverallScore}/100 ({model.GradeCode})", 2600, true, scoreText, "ctr"),
                    (Trunc(model.LocalizedGradeLabel, 34), 1100, false, "334155", "ctr")
                }));

            sb.Append(BuildRectShape(
                id++, "KpiClean", startX + (cardW + gap) * 1, cardY, cardW, cardH,
                "F0FDF4", "22C55E", "roundRect", "ctr",
                new[]
                {
                    ("CLEAN ORTHOGRAPHY %", 1050, true, "475569", "ctr"),
                    ($"{model.CleanAccuracyPercent:0.0}%", 2600, true, "15803D", "ctr"),
                    ($"{model.EstimatedCleanWords:N0} Clean Words Verified", 1100, false, "334155", "ctr")
                }));

            sb.Append(BuildRectShape(
                id++, "KpiWrong", startX + (cardW + gap) * 2, cardY, cardW, cardH,
                model.ConfirmedWrongCount == 0 ? "F0FDF4" : "FEF2F2",
                model.ConfirmedWrongCount == 0 ? "22C55E" : "EF4444",
                "roundRect", "ctr",
                new[]
                {
                    ("CONFIRMED WRONG", 1050, true, "475569", "ctr"),
                    ($"{model.ConfirmedWrongCount:N0}", 2600, true, model.ConfirmedWrongCount == 0 ? "15803D" : "B91C1C", "ctr"),
                    ($"Spell: {model.ConfirmedSpellingCount}  •  Style: {model.ConfirmedGrammarAndStyleCount}", 1050, false, "334155", "ctr")
                }));

            sb.Append(BuildRectShape(
                id++, "KpiReview", startX + (cardW + gap) * 3, cardY, cardW, cardH,
                model.SuspiciousReviewCount == 0 ? "F8FAFC" : "FFFBEB",
                model.SuspiciousReviewCount == 0 ? "CBD5E1" : "F59E0B",
                "roundRect", "ctr",
                new[]
                {
                    ("SUSPICIOUS / REVIEW", 1050, true, "475569", "ctr"),
                    ($"{model.SuspiciousReviewCount:N0}", 2600, true, model.SuspiciousReviewCount == 0 ? "334155" : "B45309", "ctr"),
                    ($"Names/OOV: {model.SuspiciousNameOrOovCount}  •  Sense: {model.SuspiciousContextMeaningCount}", 1050, false, "334155", "ctr")
                }));

            // Bottom Executive Insights Card
            string statusHeadline = (model.ConfirmedWrongCount == 0 && model.SuspiciousReviewCount == 0)
                ? "✓ Clean Orthography Verification: Zero unresolved Myanmar spelling or grammar issues found across all audited sections."
                : $"Action Summary: {model.TotalUniqueGroupsCount:N0} unique word/phrase pattern(s) identified ({model.ConfirmedWrongCount:N0} high-certainty fixes, {model.SuspiciousReviewCount:N0} human-review checks, {model.SessionFixedCount:N0} already fixed in session).";

            sb.Append(BuildRectShape(
                id++, "ExecNotes", 420000, 4020000, SlideWidthEmu - 840000, 2200000,
                "FFFFFF", "CBD5E1", "roundRect", "t",
                new[]
                {
                    ("Executive Audit Guidance & Companion Reports", 1400, true, "0F172A", "l"),
                    (statusHeadline, 1200, false, "1E293B", "l"),
                    ($"• Formal Register Score: {model.FormalRegisterPercent:0.0}%   |   Cross-Document Consistency Alerts: {model.ConsistencyWarningCount}", 1150, false, "334155", "l"),
                    ("• For pen-and-paper review, print the companion ISO A4 Portrait Word Report (.docx) with ☐ check-off boxes and repeating headers.", 1150, false, "334155", "l"),
                    ("• For bulk cell/slide filtering, open the Multi-Sheet Excel Report (.xlsx) or UTF-8 CSV (.csv).", 1150, false, "334155", "l")
                },
                leftIns: 240000, rightIns: 240000, topIns: 160000, bottomIns: 140000));

            sb.Append(EndSlideXml());
            return sb.ToString();
        }

        private static string BuildSlide2SectionBreakdownXml(ProofingAuditReportModel model)
        {
            int id = 2;
            var sb = new StringBuilder();
            sb.Append(BeginSlideXml(ref id, model, UiLocalizer.Pick("All Sheets / Slides / Pages Breakdown", "စာရွက် / ဆလိုက် / စာမျက်နှာအလိုက် စစ်ဆေးမှုစာရင်း"), 2, 4));

            long tableX = 420000;
            long tableW = SlideWidthEmu - 840000; // 11,352,000
            long headerY = 1080000;
            long rowH = 430000;

            long col1W = 4752000; // Section Name
            long col2W = 2000000; // Confirmed Wrong
            long col3W = 2000000; // Suspicious
            long col4W = 2600000; // Status

            // Table Header
            sb.Append(BuildRectShape(id++, "SecHdr1", tableX, headerY, col1W, 460000, "1E293B", "334155", "rect", "ctr",
                new[] { ("Sheet / Slide / Page", 1150, true, "FFFFFF", "l") }));
            sb.Append(BuildRectShape(id++, "SecHdr2", tableX + col1W, headerY, col2W, 460000, "1E293B", "334155", "rect", "ctr",
                new[] { ("Confirmed Wrong", 1150, true, "FFFFFF", "ctr") }));
            sb.Append(BuildRectShape(id++, "SecHdr3", tableX + col1W + col2W, headerY, col3W, 460000, "1E293B", "334155", "rect", "ctr",
                new[] { ("Review Needed", 1150, true, "FFFFFF", "ctr") }));
            sb.Append(BuildRectShape(id++, "SecHdr4", tableX + col1W + col2W + col3W, headerY, col4W, 460000, "1E293B", "334155", "rect", "ctr",
                new[] { ("Audit Status", 1150, true, "FFFFFF", "ctr") }));

            var sections = model.SectionBreakdowns.Count > 0
                ? model.SectionBreakdowns
                : new List<ProofingAuditSectionBreakdown>
                {
                    new ProofingAuditSectionBreakdown
                    {
                        SectionName = model.DocumentTitle,
                        ConfirmedWrongCount = model.ConfirmedWrongCount,
                        SuspiciousReviewCount = model.SuspiciousReviewCount
                    }
                };

            int maxRows = Math.Min(10, sections.Count);
            for (int i = 0; i < maxRows; i++)
            {
                var sec = sections[i];
                long y = headerY + 460000 + (i * rowH);
                string bg = (i % 2 == 0) ? "FFFFFF" : "F1F5F9";
                string statusText = sec.TotalIssues == 0
                    ? "✓ Clean (0 Issues)"
                    : (sec.ConfirmedWrongCount > 0 ? $"Fix {sec.ConfirmedWrongCount} + Review {sec.SuspiciousReviewCount}" : $"Review {sec.SuspiciousReviewCount}");
                string statusColor = sec.TotalIssues == 0 ? "047857" : (sec.ConfirmedWrongCount > 0 ? "B91C1C" : "B45309");

                sb.Append(BuildRectShape(id++, $"SecR{i}C1", tableX, y, col1W, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (Trunc(sec.SectionName, 48), 1100, true, "0F172A", "l") }));
                sb.Append(BuildRectShape(id++, $"SecR{i}C2", tableX + col1W, y, col2W, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (sec.ConfirmedWrongCount.ToString("N0", CultureInfo.InvariantCulture), 1100, sec.ConfirmedWrongCount > 0, sec.ConfirmedWrongCount > 0 ? "B91C1C" : "475569", "ctr") }));
                sb.Append(BuildRectShape(id++, $"SecR{i}C3", tableX + col1W + col2W, y, col3W, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (sec.SuspiciousReviewCount.ToString("N0", CultureInfo.InvariantCulture), 1100, sec.SuspiciousReviewCount > 0, sec.SuspiciousReviewCount > 0 ? "B45309" : "475569", "ctr") }));
                sb.Append(BuildRectShape(id++, $"SecR{i}C4", tableX + col1W + col2W + col3W, y, col4W, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (statusText, 1050, true, statusColor, "ctr") }));
            }

            if (sections.Count > maxRows)
            {
                int remaining = sections.Count - maxRows;
                int remClean = sections.Skip(maxRows).Count(s => s.TotalIssues == 0);
                long noteY = headerY + 460000 + (maxRows * rowH) + 80000;
                sb.Append(BuildRectShape(id++, "SecMoreNote", tableX, noteY, tableW, 420000, "EFF6FF", "93C5FD", "roundRect", "ctr",
                    new[] { ($"+ {remaining} additional sheet(s)/slide(s)/page(s) audited ({remClean} clean). Full breakdown is included in the Printable A4 Word (.docx) & Excel (.xlsx) reports.", 1050, true, "1E3A8A", "l") }));
            }

            sb.Append(EndSlideXml());
            return sb.ToString();
        }

        private static string BuildSlide3RepeatedMistakesXml(ProofingAuditReportModel model)
        {
            int id = 2;
            var sb = new StringBuilder();
            sb.Append(BeginSlideXml(ref id, model, UiLocalizer.Pick("Top Priority Repeated Findings (×Count)", "အကြိမ်ရေအများဆုံး တွေ့ရှိချက်များ (×Count)"), 3, 4));

            long tableX = 420000;
            long tableW = SlideWidthEmu - 840000;
            long headerY = 1080000;

            var groups = model.AllRepeatedGroups.Count > 0 ? model.AllRepeatedGroups : model.TopRepeatedGroups;
            if (groups.Count == 0)
            {
                sb.Append(BuildRectShape(id++, "CleanDeckCard", tableX, 1800000, tableW, 2400000, "ECFDF5", "10B981", "roundRect", "ctr",
                    new[]
                    {
                        ("✓ Zero Orthography Mistakes Found", 2200, true, "065F46", "ctr"),
                        ("All Myanmar syllables, stacked consonants, medial sequences, and formal particles passed MLC verification.", 1250, false, "047857", "ctr")
                    }));
                sb.Append(EndSlideXml());
                return sb.ToString();
            }

            long c1 = 900000;   // Count
            long c2 = 2800000;  // Found
            long c3 = 2800000;  // Fix
            long c4 = tableW - c1 - c2 - c3; // Locations + Reason

            sb.Append(BuildRectShape(id++, "GrpH1", tableX, headerY, c1, 460000, "7F1D1D", "991B1B", "rect", "ctr",
                new[] { ("×Count", 1100, true, "FFFFFF", "ctr") }));
            sb.Append(BuildRectShape(id++, "GrpH2", tableX + c1, headerY, c2, 460000, "7F1D1D", "991B1B", "rect", "ctr",
                new[] { ("Found in Document", 1100, true, "FFFFFF", "l") }));
            sb.Append(BuildRectShape(id++, "GrpH3", tableX + c1 + c2, headerY, c3, 460000, "7F1D1D", "991B1B", "rect", "ctr",
                new[] { ("Recommended MLC Fix", 1100, true, "FFFFFF", "l") }));
            sb.Append(BuildRectShape(id++, "GrpH4", tableX + c1 + c2 + c3, headerY, c4, 460000, "7F1D1D", "991B1B", "rect", "ctr",
                new[] { ("Sample Locations & Rule", 1100, true, "FFFFFF", "l") }));

            int maxRows = Math.Min(8, groups.Count);
            long rowH = 540000;
            for (int i = 0; i < maxRows; i++)
            {
                var g = groups[i];
                long y = headerY + 460000 + (i * rowH);
                string bg = (i % 2 == 0) ? "FFFFFF" : "FEF2F2";

                sb.Append(BuildRectShape(id++, $"GrpR{i}C1", tableX, y, c1, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { ($"×{g.Count}", 1150, true, g.IsConfirmedWrong ? "B91C1C" : "B45309", "ctr") }));
                sb.Append(BuildRectShape(id++, $"GrpR{i}C2", tableX + c1, y, c2, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (Trunc(g.VisualOriginal, 28), 1150, true, "991B1B", "l") }));
                sb.Append(BuildRectShape(id++, $"GrpR{i}C3", tableX + c1 + c2, y, c3, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (Trunc(g.RecommendedFix, 28), 1150, true, "047857", "l") }));
                sb.Append(BuildRectShape(id++, $"GrpR{i}C4", tableX + c1 + c2 + c3, y, c4, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (Trunc($"{g.SampleLocations} — {g.PrimaryReason}", 58), 1000, false, "334155", "l") }));
            }

            if (groups.Count > maxRows)
            {
                long noteY = headerY + 460000 + (maxRows * rowH) + 70000;
                sb.Append(BuildRectShape(id++, "GrpMore", tableX, noteY, tableW, 400000, "FFFBEB", "FCD34D", "roundRect", "ctr",
                    new[] { ($"+ {groups.Count - maxRows} more unique word pattern(s) documented in the Printable A4 Word (.docx) & Multi-Sheet Excel (.xlsx) reports.", 1050, true, "92400E", "l") }));
            }

            sb.Append(EndSlideXml());
            return sb.ToString();
        }

        private static string BuildSlide4KeyHighlightsXml(ProofingAuditReportModel model)
        {
            int id = 2;
            var sb = new StringBuilder();
            sb.Append(BeginSlideXml(ref id, model, UiLocalizer.Pick("Detailed Findings Sample & Sign-Off", "အသေးစိတ် တွေ့ရှိချက် နမူနာနှင့် အတည်ပြုချက်"), 4, 4));

            long tableX = 420000;
            long tableW = SlideWidthEmu - 840000;
            long headerY = 1080000;

            var items = model.ConfirmedWrongItems.Concat(model.SuspiciousReviewItems).Take(7).ToList();
            if (items.Count == 0)
            {
                sb.Append(BuildRectShape(id++, "SignOffClean", tableX, 1600000, tableW, 3000000, "FFFFFF", "10B981", "roundRect", "ctr",
                    new[]
                    {
                        ("✓ Official Proofreading Clearance Ready", 2000, true, "065F46", "ctr"),
                        ($"Audited Sections: {Math.Max(1, model.SectionBreakdowns.Count)}   •   Session Fixes Applied: {model.SessionFixedCount}", 1300, true, "1E293B", "ctr"),
                        ("Print the companion ISO A4 Word Report (.docx) for physical reviewer signature and filing.", 1200, false, "475569", "ctr")
                    }));
                sb.Append(EndSlideXml());
                return sb.ToString();
            }

            long c1 = 2200000; // Location
            long c2 = 2400000; // Found
            long c3 = 2400000; // Fix
            long c4 = tableW - c1 - c2 - c3; // Explanation

            sb.Append(BuildRectShape(id++, "DetH1", tableX, headerY, c1, 450000, "0F172A", "334155", "rect", "ctr",
                new[] { ("Location", 1100, true, "FFFFFF", "l") }));
            sb.Append(BuildRectShape(id++, "DetH2", tableX + c1, headerY, c2, 450000, "0F172A", "334155", "rect", "ctr",
                new[] { ("Found", 1100, true, "FFFFFF", "l") }));
            sb.Append(BuildRectShape(id++, "DetH3", tableX + c1 + c2, headerY, c3, 450000, "0F172A", "334155", "rect", "ctr",
                new[] { ("Correct Fix", 1100, true, "FFFFFF", "l") }));
            sb.Append(BuildRectShape(id++, "DetH4", tableX + c1 + c2 + c3, headerY, c4, 450000, "0F172A", "334155", "rect", "ctr",
                new[] { ("Category & Orthography Rule", 1100, true, "FFFFFF", "l") }));

            long rowH = 520000;
            for (int i = 0; i < items.Count; i++)
            {
                var row = items[i];
                long y = headerY + 450000 + (i * rowH);
                string bg = (i % 2 == 0) ? "FFFFFF" : "F8FAFC";

                sb.Append(BuildRectShape(id++, $"DetR{i}C1", tableX, y, c1, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (Trunc(row.LocationLabel, 24), 1000, true, "1E293B", "l") }));
                sb.Append(BuildRectShape(id++, $"DetR{i}C2", tableX + c1, y, c2, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (Trunc(row.VisualOriginal, 24), 1100, true, row.IsConfirmedWrong ? "B91C1C" : "B45309", "l") }));
                sb.Append(BuildRectShape(id++, $"DetR{i}C3", tableX + c1 + c2, y, c3, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (Trunc(row.RecommendedFix, 24), 1100, true, "047857", "l") }));
                sb.Append(BuildRectShape(id++, $"DetR{i}C4", tableX + c1 + c2 + c3, y, c4, rowH, bg, "CBD5E1", "rect", "ctr",
                    new[] { (Trunc($"{row.CategoryLabel}: {row.Explanation}", 52), 950, false, "334155", "l") }));
            }

            long signY = headerY + 450000 + (items.Count * rowH) + 100000;
            sb.Append(BuildRectShape(id++, "SignStrip", tableX, signY, tableW, 560000, "EFF6FF", "93C5FD", "roundRect", "ctr",
                new[]
                {
                    ($"Full Printable Checklist: Export 'Printable A4 Word Report (.docx)' for all {model.AllItemsInDocumentOrder.Count:N0} finding(s), ☐ pen check-off boxes, and signature block.", 1050, true, "1E3A8A", "l")
                }));

            sb.Append(EndSlideXml());
            return sb.ToString();
        }
    }
}
