// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace MyanmarSpellChecker.Core.Official
{
    /// <summary>
    /// Single-source-of-truth Bilingual (English + မြန်မာဘာသာ) Illustrated User Guide generator and launcher for the
    /// Myanmar Spell Checker 3-in-1 Microsoft Office Suite (Word + Excel + PowerPoint).
    /// Produces three 100% offline formats with annotated UI screenshots, numbered callout badges, and bilingual captions:
    /// 1. GitHub Flavored Markdown (<c>docs/USER_GUIDE.md</c>) with linked <c>images/fig01..fig08.png</c> &amp; callout keys
    /// 2. Interactive Searchable Offline HTML Handbook (<c>Myanmar-Spell-Checker-User-Guide.html</c>) with embedded Base64
    ///    screenshots, click-to-zoom lightbox, live search filter, [EN + မြန်မာ | English | မြန်မာ] toggle &amp; Print CSS
    /// 3. Print-Ready ISO A4 Portrait Microsoft Word Manual (<c>Myanmar-Spell-Checker-User-Guide.docx</c>) with embedded
    ///    OpenXML <c>&lt;w:drawing&gt;</c> screenshots, shaded figure caption callout boxes, repeating headers &amp; Pyidaungsu typography.
    /// </summary>
    public static class OfflineUserGuideExporter
    {
        public const string HtmlGuideFileName = "Myanmar-Spell-Checker-User-Guide.html";
        public const string DocxGuideFileName = "Myanmar-Spell-Checker-User-Guide.docx";
        public const string MarkdownGuideFileName = "USER_GUIDE.md";

        private const int IsoA4WidthTwips = 11906;   // 210.0 mm
        private const int IsoA4HeightTwips = 16838;  // 297.0 mm
        private const int MarginTopTwips = 850;
        private const int MarginBottomTwips = 850;
        private const int MarginSideTwips = 794;
        private const int ContentWidthTwips = IsoA4WidthTwips - (MarginSideTwips * 2); // 10,318 twips (~182 mm)
        private const long MaxFigureWidthEmus = 6020000L; // ~167.2 mm inside A4 figure card margins

        public sealed class GuideCallout
        {
            public int Number { get; set; }
            public string LabelEn { get; set; } = string.Empty;
            public string ExplainEn { get; set; } = string.Empty;
            public string LabelMm { get; set; } = string.Empty;
            public string ExplainMm { get; set; } = string.Empty;
        }

        public sealed class GuideFigure
        {
            public int FigureIndex { get; set; }
            public string ImageFileName { get; set; } = string.Empty;
            public string TitleEn { get; set; } = string.Empty;
            public string TitleMm { get; set; } = string.Empty;
            public string CaptionEn { get; set; } = string.Empty;
            public string CaptionMm { get; set; } = string.Empty;
            public List<GuideCallout> Callouts { get; } = new List<GuideCallout>();
        }

        public sealed class GuideSubsection
        {
            public string HeadingEn { get; set; } = string.Empty;
            public string HeadingMm { get; set; } = string.Empty;
            public List<(string En, string Mm)> Bullets { get; } = new List<(string En, string Mm)>();
            public string[]? TableHeaders { get; set; }
            public List<string[]> TableRows { get; } = new List<string[]>();
        }

        public sealed class GuideChapter
        {
            public string Id { get; set; } = string.Empty;
            public string Number { get; set; } = string.Empty;
            public string TitleEn { get; set; } = string.Empty;
            public string TitleMm { get; set; } = string.Empty;
            public string SummaryEn { get; set; } = string.Empty;
            public string SummaryMm { get; set; } = string.Empty;
            public List<GuideFigure> Figures { get; } = new List<GuideFigure>();
            public List<GuideSubsection> Subsections { get; } = new List<GuideSubsection>();
        }

        public static string GetDefaultUserGuideDirectory()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyanmarSpellChecker",
                "Guide");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string? FindDefaultImagesDirectory(string? explicitImagesDir = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitImagesDir) && Directory.Exists(explicitImagesDir))
                return Path.GetFullPath(explicitImagesDir);

            string[] roots =
            {
                Directory.GetCurrentDirectory(),
                AppDomain.CurrentDomain.BaseDirectory
            };

            foreach (string start in roots)
            {
                if (string.IsNullOrWhiteSpace(start)) continue;
                try
                {
                    var dir = new DirectoryInfo(start);
                    for (int depth = 0; depth < 7 && dir != null; depth++, dir = dir.Parent)
                    {
                        string candidate = Path.Combine(dir.FullName, "docs", "images");
                        if (Directory.Exists(candidate))
                            return candidate;
                    }
                }
                catch { }
            }

            return null;
        }

        public static string EnsureInteractiveHtmlGuideOnDisk(string? directoryOverride = null, string? imagesDir = null)
        {
            string dir = !string.IsNullOrWhiteSpace(directoryOverride)
                ? directoryOverride!
                : GetDefaultUserGuideDirectory();
            Directory.CreateDirectory(dir);
            string resolvedImages = FindDefaultImagesDirectory(imagesDir ?? Path.Combine(dir, "images")) ?? string.Empty;
            string fullPath = Path.Combine(dir, HtmlGuideFileName);
            File.WriteAllText(
                fullPath,
                BuildInteractiveHtmlUserGuide(string.IsNullOrEmpty(resolvedImages) ? null : resolvedImages),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return fullPath;
        }

        public static string EnsurePrintableWordGuideOnDisk(string? directoryOverride = null, string? imagesDir = null)
        {
            string dir = !string.IsNullOrWhiteSpace(directoryOverride)
                ? directoryOverride!
                : GetDefaultUserGuideDirectory();
            Directory.CreateDirectory(dir);
            string resolvedImages = FindDefaultImagesDirectory(imagesDir ?? Path.Combine(dir, "images")) ?? string.Empty;
            string fullPath = Path.Combine(dir, DocxGuideFileName);
            File.WriteAllBytes(
                fullPath,
                BuildPrintableA4WordUserGuideBytes(string.IsNullOrEmpty(resolvedImages) ? null : resolvedImages));
            return fullPath;
        }

        public static string EnsureMarkdownGuideOnDisk(string directoryPath, string? imagesDir = null)
        {
            Directory.CreateDirectory(directoryPath);
            _ = imagesDir;
            string fullPath = Path.Combine(directoryPath, MarkdownGuideFileName);
            File.WriteAllText(fullPath, BuildMarkdownUserGuide(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return fullPath;
        }

        public static string OpenInteractiveHtmlGuide(string? preferredSearchDir = null)
        {
            string dir = GetDefaultUserGuideDirectory();
            string targetHtml = Path.Combine(dir, HtmlGuideFileName);
            string targetDocx = Path.Combine(dir, DocxGuideFileName);

            TrySyncPrebuiltGuideIfNewer(preferredSearchDir, HtmlGuideFileName, targetHtml);
            TrySyncPrebuiltGuideIfNewer(preferredSearchDir, DocxGuideFileName, targetDocx);

            string? imagesDir = FindDefaultImagesDirectory();
            if (!File.Exists(targetDocx) || !string.IsNullOrEmpty(imagesDir))
            {
                try { EnsurePrintableWordGuideOnDisk(dir, imagesDir); } catch { }
            }

            bool hasRichEmbeddedHtml = File.Exists(targetHtml) && new FileInfo(targetHtml).Length > 100_000;
            if (!hasRichEmbeddedHtml || !string.IsNullOrEmpty(imagesDir))
            {
                targetHtml = EnsureInteractiveHtmlGuideOnDisk(dir, imagesDir);
            }

            Process.Start(new ProcessStartInfo(targetHtml)
            {
                UseShellExecute = true
            });
            return targetHtml;
        }

        public static string OpenPrintableWordGuide(string? preferredSearchDir = null)
        {
            string dir = GetDefaultUserGuideDirectory();
            string targetDocx = Path.Combine(dir, DocxGuideFileName);
            TrySyncPrebuiltGuideIfNewer(preferredSearchDir, DocxGuideFileName, targetDocx);

            string? imagesDir = FindDefaultImagesDirectory();
            bool hasRichEmbeddedDocx = File.Exists(targetDocx) && new FileInfo(targetDocx).Length > 60_000;
            if (!hasRichEmbeddedDocx || !string.IsNullOrEmpty(imagesDir))
            {
                targetDocx = EnsurePrintableWordGuideOnDisk(dir, imagesDir);
            }

            Process.Start(new ProcessStartInfo(targetDocx)
            {
                UseShellExecute = true
            });
            return targetDocx;
        }

        private static void TrySyncPrebuiltGuideIfNewer(string? preferredDir, string fileName, string destinationPath)
        {
            try
            {
                var candidates = new List<string>(6);
                if (!string.IsNullOrWhiteSpace(preferredDir))
                    candidates.Add(Path.Combine(preferredDir!, fileName));

                string localApp = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyanmarSpellChecker",
                    "app",
                    fileName);
                candidates.Add(localApp);

                string? imgDir = FindDefaultImagesDirectory();
                if (!string.IsNullOrEmpty(imgDir))
                {
                    string? docsParent = Path.GetDirectoryName(imgDir);
                    if (!string.IsNullOrEmpty(docsParent))
                        candidates.Add(Path.Combine(docsParent!, fileName));
                }

                foreach (string src in candidates)
                {
                    if (!File.Exists(src)) continue;
                    string srcFull = Path.GetFullPath(src);
                    string dstFull = Path.GetFullPath(destinationPath);
                    if (string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var srcInfo = new FileInfo(srcFull);
                    var dstInfo = new FileInfo(dstFull);
                    if (!dstInfo.Exists || srcInfo.Length > dstInfo.Length || srcInfo.LastWriteTimeUtc > dstInfo.LastWriteTimeUtc)
                    {
                        File.Copy(srcFull, dstFull, overwrite: true);
                        break;
                    }
                }
            }
            catch { }
        }

        public static IReadOnlyList<GuideChapter> BuildStructuredChapters()
        {
            var chapters = new List<GuideChapter>(8);

            // ==================== CHAPTER 1: QUICK START & INSTALLATION ====================
            var ch1 = new GuideChapter
            {
                Id = "quick-start",
                Number = "01",
                TitleEn = "Quick-Start & 1-Click Offline Installation",
                TitleMm = "အမြန်စတင်အသုံးပြုနည်းနှင့် Offline ထည့်သွင်းနည်း",
                SummaryEn = "Install once in 10 seconds with zero internet, zero admin password, and zero .NET SDK — works across Microsoft Word, Excel, and PowerPoint.",
                SummaryMm = "အင်တာနက်မလို၊ Admin Password မလို၊ .NET SDK မလိုဘဲ စက္ကန့်ပိုင်းအတွင်း သွင်းယူ၍ Microsoft Word၊ Excel နှင့် PowerPoint သုံးမျိုးလုံးတွင် တစ်ပြိုင်နက် အသုံးပြုနိုင်ပါသည်။"
            };
            var fig1 = new GuideFigure
            {
                FigureIndex = 1,
                ImageFileName = "fig01-ribbon-and-workspace-overview.png",
                TitleEn = "Unified Microsoft Office Ribbon & 4-Tab Task Pane Workspace",
                TitleMm = "အပေါ်ဘက် Office Ribbon တက်ဘ်နှင့် ညာဘက် တက်ဘ် (၄) ခုပါ Task Pane မြင်ကွင်း",
                CaptionEn = "Opening Word, Excel, or PowerPoint immediately activates the top 'Myanmar Spell Checker' Ribbon tab alongside the docked right-hand 4-Tab Workspace.",
                CaptionMm = "Word, Excel သို့မဟုတ် PowerPoint ဖွင့်လိုက်သည်နှင့် အပေါ်ဘက် 'Myanmar Spell Checker' Ribbon နှင့် ညာဘက် Task Pane တို့ အသင့်ပေါ်လာပုံ။"
            };
            fig1.Callouts.Add(new GuideCallout { Number = 1, LabelEn = "Myanmar Spell Checker Ribbon Tab", ExplainEn = "1-click access to Scan Document, Scan Selection, Fix All, Clear Marks, Style Mode, Dictionary, Bilingual UI toggle, and Offline User Guide.", LabelMm = "အပေါ်ဘက် Ribbon ခလုတ်တန်း", ExplainMm = "စာတမ်းစစ်ဆေးရန်၊ ရွေးကွက်စစ်ရန်၊ အားလုံးပြင်ရန်၊ အဘိဓာန်နှင့် လမ်းညွှန်စာအုပ်ဖွင့်ရန် ခလုတ်များ။" });
            fig1.Callouts.Add(new GuideCallout { Number = 2, LabelEn = "Live Bilingual UI & Eco Header Pills", ExplainEn = "Click [A/က EN ⇄ မြန်မာ] anytime to switch the entire interface live without restarting Office; [Eco] keeps 4 GB RAM PCs smooth.", LabelMm = "[A/က EN ⇄ မြန်မာ] နှင့် Eco ခလုတ်", ExplainMm = "Office ပြန်ပိတ်စရာမလိုဘဲ မြန်မာ/အင်္ဂလိပ် ချက်ချင်းပြောင်းနိုင်ပြီး စက်မလေးစေရန် Eco စနစ် ထိန်းညှိပေးသည်။" });
            fig1.Callouts.Add(new GuideCallout { Number = 3, LabelEn = "4-Tab Workspace Bar", ExplainEn = "Switch seamlessly between Proofing (စာစစ်), Dictionary (အဘိဓာန်), Official Toolkit (ကိရိယာ), and Custom Words (မှတ်သားစာ).", LabelMm = "လုပ်ငန်းခွင် တက်ဘ် (၄) ခု", ExplainMm = "စာစစ်၊ အဘိဓာန်၊ ရုံးစာကိရိယာ နှင့် ကိုယ်ပိုင်မှတ်သားစာ တက်ဘ် ၄ ခုသို့ အလွယ်တကူ ကူးပြောင်းနိုင်သည်။" });
            fig1.Callouts.Add(new GuideCallout { Number = 4, LabelEn = "3-Mode Writing Style Selector", ExplainEn = "Choose Casual (ပေါ့ပါးဟန်), Standard (အထွေထွေစံ), or Official (ရုံးသုံးစာပေ) to match your document's audience.", LabelMm = "အရေးအသားစနစ် (၃) မျိုး ရွေးခလုတ်", ExplainMm = "စာအမျိုးအစားအလိုက် ပေါ့ပါးဟန်၊ အထွေထွေစံ သို့မဟုတ် ရုံးသုံးစာပေဟန် ရွေးချယ်နိုင်သည်။" });
            fig1.Callouts.Add(new GuideCallout { Number = 5, LabelEn = "Document Canvas Wavy Underlines", ExplainEn = "Color-coded Red (Spelling), Blue (Grammar/Style), and Amber (Contextual Review) squiggles appear directly under flagged Burmese spans.", LabelMm = "စာမျက်နှာပေါ်ရှိ အရောင်ခွဲ မျဉ်းလှိုင်းများ", ExplainMm = "သတ်ပုံအမှား (အနီ)၊ သဒ္ဒါ/စာဟန် (အပြာ) နှင့် အနက်သံသယ (လိမ္မော်) တို့ကို စာသားအောက်တွင် တိုက်ရိုက်ပြသပေးသည်။" });
            ch1.Figures.Add(fig1);

            var ch1s1 = new GuideSubsection
            {
                HeadingEn = "1.1 System Requirements & 3-Step Setup",
                HeadingMm = "၁.၁ လိုအပ်သော စနစ်များနှင့် အဆင့် (၃) ဆင့်ဖြင့် သွင်းယူနည်း"
            };
            ch1s1.Bullets.Add((
                "Supported System: Windows 10 or Windows 11 (64-bit or 32-bit) with Microsoft Office 2016 / 2019 / 2021 / Microsoft 365 Desktop (Word, Excel & PowerPoint). .NET Framework 4.8 is already built into Windows 10/11.",
                "အသုံးပြုနိုင်သော စနစ် — Windows 10 သို့မဟုတ် Windows 11 (64-bit / 32-bit) နှင့် Microsoft Office 2016 / 2019 / 2021 / 365 (Word, Excel, PowerPoint) တို့တွင် အသုံးပြုနိုင်ပါသည်။"
            ));
            ch1s1.Bullets.Add((
                "Step 1 (Extract First): Right-click the downloaded ZIP file and click 'Extract All...' into a normal folder before running Setup.",
                "အဆင့် ၁ (ZIP အရင်ဖြည်ပါ) — ဒေါင်းလုဒ်ဆွဲထားသော .ZIP ဖိုင်ပေါ်တွင် Right-click နှိပ်ပြီး 'Extract All...' ဖြင့် ဖိုဒါအဖြစ် အရင်ဖြည်ပါ။"
            ));
            ch1s1.Bullets.Add((
                "Step 2 (Close Office & Double-Click Setup): Save open files, close Word/Excel/PowerPoint, and double-click 'Install-MyanmarSpellChecker.bat' (or 'Install-EasyUser.bat').",
                "အဆင့် ၂ (Office ခေတ္တပိတ်၍ သွင်းပါ) — Word, Excel, PowerPoint တို့ကို ခေတ္တပိတ်ပြီး 'Install-MyanmarSpellChecker.bat' သို့မဟုတ် 'Install-EasyUser.bat' ကို Double-click နှိပ်ပါ။"
            ));
            ch1s1.Bullets.Add((
                "Step 3 (Open Office & Start): Open Word, Excel, or PowerPoint! You will see the 'Myanmar Spell Checker' Ribbon tab at the top, a quick launcher inside Office's 'Review' tab, and the 4-Tab Task Pane on the right.",
                "အဆင့် ၃ (စတင်အသုံးပြုပါ) — Word, Excel သို့မဟုတ် PowerPoint ဖွင့်လိုက်သည်နှင့် အပေါ်ဘက်တွင် 'Myanmar Spell Checker' တက်ဘ်၊ 'Review' တက်ဘ်အတွင်း အမြန်ဖွင့်ခလုတ် နှင့် ညာဘက် Task Pane ပြကွက်တို့ကို တွေ့ရပါမည်။"
            ));
            ch1s1.Bullets.Add((
                "Instant Bilingual UI Switch ([A/က EN ⇄ မြန်မာ]): Click the [A/က EN] button in the Task Pane header (or Ribbon) anytime to toggle every button, tooltip, and explanation live between English and Myanmar without restarting Office.",
                "ဘာသာစကား ချက်ချင်းပြောင်းခြင်း ([A/က EN ⇄ မြန်မာ]) — ညာဘက်ပြကွက်ထိပ်ရှိ [A/က EN] ခလုတ်ကို နှိပ်ရုံဖြင့် စနစ်တစ်ခုလုံးကို အင်္ဂလိပ် ⇄ မြန်မာ အချိန်မရွေး တိုက်ရိုက်ပြောင်းလဲနိုင်ပါသည်။"
            ));
            ch1.Subsections.Add(ch1s1);
            chapters.Add(ch1);

            // ==================== CHAPTER 2: COMPLETE TOOL DIRECTORY ====================
            var ch2 = new GuideChapter
            {
                Id = "all-tools",
                Number = "02",
                TitleEn = "Complete Directory of Available Tools (28 Built-In Tools)",
                TitleMm = "ပါဝင်သော ကိရိယာများ အားလုံးစာရင်း (စုစုပေါင်း ၂၈ မျိုး)",
                SummaryEn = "Every tool available across the 4-Tab Task Pane (Proofing, Dictionary, Toolkit, Custom) and the Ribbon across Word, Excel, and PowerPoint.",
                SummaryMm = "ညာဘက် Task Pane တက်ဘ် (၄) ခု (Proofing, Dictionary, Toolkit, Custom) နှင့် အပေါ်ဘက် Ribbon တို့တွင် ပါဝင်သော ကိရိယာ (၂၈) မျိုးလုံး၏ လုပ်ဆောင်ချက်များ။"
            };
            var fig2 = new GuideFigure
            {
                FigureIndex = 2,
                ImageFileName = "fig02-word-live-proofing-and-quickfix.png",
                TitleEn = "Live Proofing Cards, Category Filters & Floating On-Cursor Quick-Fix Popup",
                TitleMm = "စာစစ်ကတ်ပြားများ၊ အမျိုးအစား စစ်ထုတ်ခလုတ်များနှင့် စာလုံးအောက် တိုက်ရိုက်ပေါ်သော အမြန်ပြင်ပြကွက်",
                CaptionEn = "Click any underlined word on the page to open the floating Quick-Fix Popup right beneath your cursor, or step through rich explanatory cards in the Proofing tab.",
                CaptionMm = "မျဉ်းတားထားသော စာလုံးပေါ် နှိပ်လိုက်သည်နှင့် အကြံပြုချက် ရွေးချယ်စရာ Quick-Fix Popup ချက်ချင်းပေါ်လာသကဲ့သို့ ညာဘက် စာစစ်ကတ်ပြားများမှလည်း တစ်ချက်နှိပ် ပြင်ဆင်နိုင်ပါသည်။"
            };
            fig2.Callouts.Add(new GuideCallout { Number = 1, LabelEn = "Underlined Span in Document", ExplainEn = "Click or right-click any Red, Blue, or Amber wavy underline to inspect replacements in place without losing reading flow.", LabelMm = "စာသားအတွင်းရှိ အမှားမျဉ်းလှိုင်း", ExplainMm = "အနီ၊ အပြာ သို့မဟုတ် လိမ္မော်မျဉ်းပေါ် နှိပ်ရုံဖြင့် စာဖတ်မပျက် တိုက်ရိုက်ပြင်ဆင်နိုင်သည်။" });
            fig2.Callouts.Add(new GuideCallout { Number = 2, LabelEn = "Floating On-Cursor Quick-Fix Popup", ExplainEn = "Places ranked green replacement buttons at the very top with meaning cues, rule notes, [Ignore], and [+ Add Word].", LabelMm = "စာလုံးအောက် တိုက်ရိုက်ပေါ်သော Quick-Fix ပြကွက်", ExplainMm = "အမှန်အကြံပြုချက် ခလုတ်စိမ်းများ၊ အဓိပ္ပာယ်ရှင်းလင်းချက်၊ [ကျော်မည်] နှင့် [+ မှတ်သားမည်] တို့ ပါဝင်သည်။" });
            fig2.Callouts.Add(new GuideCallout { Number = 3, LabelEn = "Category Filter & ↑ / ↓ Issue Stepper", ExplainEn = "Filter findings by All, Spelling (သတ်ပုံ), Grammar (သဒ္ဒါ), or Context (အနက်) and step smoothly from issue to issue.", LabelMm = "အမှားအမျိုးအစား ခွဲထုတ်ခလုတ်နှင့် ↑ / ↓ ခလုတ်", ExplainMm = "သတ်ပုံ၊ သဒ္ဒါ၊ အနက် အလိုက် သီးသန့်စစ်ထုတ်၍ တစ်ခုချင်း အစဉ်လိုက် သွားရောက်စစ်ဆေးနိုင်သည်။" });
            fig2.Callouts.Add(new GuideCallout { Number = 4, LabelEn = "Proofing Issue Card & Diff Badge", ExplainEn = "Shows exact location (Page/Para), visual diff tags ([ါ➔ာ], [ZWSP]), official သတ်ပုံကျမ်း explanation, and 1-click fix buttons.", LabelMm = "အသေးစိတ် ရှင်းလင်းချက်ပါ စာစစ်ကတ်ပြား", ExplainMm = "စာမျက်နှာ/စာပိုဒ်နေရာ၊ ကွဲလွဲချက်အမှတ်အသား ([ါ➔ာ], [ZWSP]) နှင့် သတ်ပုံကျမ်းရှင်းလင်းချက် အပြည့်အစုံ ပြသပေးသည်။" });
            fig2.Callouts.Add(new GuideCallout { Number = 5, LabelEn = "Review & Fix / Export Audit Toolbar", ExplainEn = "Launch the checklist studio for safe batch fixing or click [≡ Report ▾] to export a 5-format QA audit certificate.", LabelMm = "အားလုံးပြင် စိစစ်ပြကွက်နှင့် အစီရင်ခံစာ ခလုတ်", ExplainMm = "အမှားအားလုံး စုပေါင်းစိစစ်ပြင်ဆင်ရန်နှင့် A4 အစီရင်ခံစာ (၅) မျိုး ထုတ်ယူရန် ခလုတ်။" });
            ch2.Figures.Add(fig2);

            var ch2s1 = new GuideSubsection
            {
                HeadingEn = "2.1 Workspace Tabs & Tool Reference Matrix",
                HeadingMm = "၂.၁ ကိရိယာတစ်ခုချင်းစီ၏ တည်နေရာနှင့် အသုံးဝင်ပုံ ဇယား",
                TableHeaders = new[] { "Location / Tab", "Tool Name (EN / မြန်မာ)", "Office Hosts", "What It Does & When to Use It" }
            };
            ch2s1.TableRows.AddRange(new[]
            {
                new[] { "Proofing (စာစစ်)", "Scan Doc / Sheet / Slide (စစ်ဆေးမည်)", "Word, Excel, PPT", "Scans full document, active sheet/workbook, or slide/deck/notes for spelling, grammar & meaning issues. (စာတမ်း၊ Sheet သို့မဟုတ် Slide အားလုံးကို စစ်ဆေးပေးသည်။)" },
                new[] { "Proofing (စာစစ်)", "Scan Selection (ရွေးကွက် စစ်မည်)", "Word, Excel, PPT", "Checks only highlighted paragraphs, selected cells, or selected slide shapes. (ရွေးချယ်ထားသော အပိုင်းကိုသာ သီးသန့် အမြန်စစ်ဆေးသည်။)" },
                new[] { "Proofing (စာစစ်)", "3-Mode Style Selector: Casual | Standard | Official", "Word, Excel, PPT", "Switches proofing strictness between chat/casual, general standard, and formal ministry/literary Burmese. (ပေါ့ပါးဟန်၊ အထွေထွေစံ နှင့် ရုံးသုံးစာပေဟန် ၃ မျိုး ပြောင်းနိုင်သည်။)" },
                new[] { "Proofing (စာစစ်)", "Review & Fix / Fix All Studio (အားလုံးပြင် စိစစ်ပြကွက်)", "Word, Excel, PPT", "Opens docked or floating checklist with checkboxes, 'Select Sure Only', search filter & 1-step Undo. (အမှားများကို အမှန်ခြစ်ရွေး၍ စိတ်ချစွာ တစ်ပြိုင်နက် ပြင်ဆင်နိုင်သည်။)" },
                new[] { "Proofing (စာစစ်)", "On-Cursor Quick-Fix Popup & Right-Click Menu", "Word, Excel, PPT", "Click or right-click any flagged word/cell/textbox to pick top ranked suggestions, Ignore, or +Add Word. (အနီ/အပြာမျဉ်းပေါ် နှိပ်ရုံဖြင့် အကြံပြုချက် ရွေးနိုင်သည်။)" },
                new[] { "Proofing (စာစစ်)", "Undo / Restore (Ctrl+Z / ↶ ပြန်ဆုတ်)", "Word, Excel, PPT", "Reverts single or batch fixes cleanly (native Ctrl+Z in Word; 25-step Undo in Excel; 30-step Undo in PPT). (ပြင်ပြီးသားကို မူလအတိုင်း ပြန်ဆုတ်နိုင်သည်။)" },
                new[] { "Proofing (စာစစ်)", "Clear Marks (မျဉ်းရှင်းမည်)", "Word, Excel, PPT", "Hides all temporary proofing underlines and suppresses stray Word English red squiggles on Burmese words. (စစ်ဆေးရေးမျဉ်းများကို ချက်ချင်းဖယ်ရှားပေးသည်။)" },
                new[] { "Proofing (စာစစ်)", "Category Filters & ↑ / ↓ Stepper", "Word, Excel, PPT", "Filters cards by All, Spelling (သတ်ပုံ), Grammar (သဒ္ဒါ), or Context (အနက်) and jumps issue-by-issue. (အမှားအမျိုးအစားအလိုက် ခွဲထုတ်ကြည့်ရှုနိုင်သည်။)" },
                new[] { "Proofing (စာစစ်)", "≡ Report ▾ (5-Format Quality Audit Exporter)", "Word, Excel, PPT", "Exports Printable A4 Word (.docx), Multi-Sheet Excel (.xlsx), Executive PPT (.pptx), CSV, or HTML/PDF report. (A4 ပုံနှိပ်အသင့် အစီရင်ခံစာ ၅ မျိုး ထုတ်ပေးသည်။)" },
                new[] { "Dictionary (အဘိဓာန်)", "183,038-Entry Bilingual EN ⇄ MM Search", "Word, Excel, PPT", "Instant offline lookup by Myanmar, English, Romanized Burmese ('mingalaba', 'kyaukse'), or Zawgyi. (မြန်မာ၊ အင်္ဂလိပ်၊ ရိုမန်အသံထွက် တို့ဖြင့် ရှာနိုင်သည်။)" },
                new[] { "Dictionary (အဘိဓာန်)", "Dual Spoken Sandhi + IPA & ▶ EN Audio TTS", "Word, Excel, PPT", "Shows natural Burmese spoken pronunciation + IPA and speaks English headwords aloud offline. (မြန်မာအသံထွက်နှင့် အင်္ဂလိပ် အသံထွက် ▶ EN နားထောင်နိုင်သည်။)" },
                new[] { "Dictionary (အဘိဓာန်)", "25,001 Encyclopedia, 1,929 Proverbs & 8 Domains", "Word, Excel, PPT", "Filter by သတ်ပုံကျမ်း, အဘိဓာန်, ဗဟုသုတ Encyclopedia, 8 Domains (Geology ဘူမိဗေဒ, Legal, Medical...), Gazetteer & Proverbs. (စွယ်စုံကျမ်း၊ စကားပုံနှင့် နယ်ပယ် ၈ ခု ပါဝင်သည်။)" },
                new[] { "Dictionary (အဘိဓာန်)", "Corpus Collocations (▸), Synonyms & ★ Bookmarks", "Word, Excel, PPT", "Displays real-world word pairings from 109,880 bigrams and syncs ★ Saved words across Office. (တွဲသုံးလေ့ရှိသော စကားလုံးများနှင့် ★ မှတ်သားချက်များ ပါဝင်သည်။)" },
                new[] { "Toolkit (ကိရိယာ)", "WinInnwa / Zawgyi ➔ Pyidaungsu Converter", "Word, Excel, PPT", "Converts legacy fonts to standard Unicode while preserving English fonts, bold/colors, and Excel formulas. (အင်္ဂလိပ်ဖောင့်နှင့် Formula မပျက်စေဘဲ ယူနီကုဒ်ပြောင်းပေးသည်။)" },
                new[] { "Toolkit (ကိရိယာ)", "Repair Garbled PDF & Paste Clean (PDF/ZG)", "Word, Excel, PPT", "Reconstructs broken Burmese PDF copy-paste (detached ေ, ြ, kinzi, vowel swaps) in place or from Clipboard. (PDF မှ ကူးယူရာတွင် ပျက်နေသော မြန်မာစာများကို ပြန်ဆက်ပေးသည်။)" },
                new[] { "Toolkit (ကိရိယာ)", "Smart Numerals 0-9 ➔ ၀-၉ (Formula-Safe)", "Word, Excel, PPT", "Converts digits to Myanmar numerals while protecting URLs, emails, ISO-9001, COVID-19, A4, 5G & numeric cells. (URL, A4, 5G နှင့် တွက်ချက်မှုဂဏန်းများ မထိခိုက်ဘဲ မြန်မာဂဏန်းပြောင်းသည်။)" },
                new[] { "Toolkit (ကိရိယာ)", "Clean Punctuation (၊ / ။) & Invisible ZWSP", "Word, Excel, PPT", "Normalizes Myanmar comma/full-stop spacing and cleans stray zero-width characters. (ပုဒ်ထီး၊ ပုဒ်မ အကွာအဝေးနှင့် ZWSP သန့်စင်ပေးသည်။)" },
                new[] { "Toolkit (ကိရိယာ)", "Rewrite Colloquial ➔ Official Prose", "Word, Excel, PPT", "Transforms spoken endings/particles (ပါတယ် ➔ ပါသည်, တွေ ➔ များ, နဲ့ ➔ နှင့်) into formal prose. (စကားပြောဟန်ကို ရုံးသုံးစာပေဟန်သို့ ပြောင်းပေးသည်။)" },
                new[] { "Toolkit (ကိရိယာ)", "Unify Myanmar Font & Smart Slide Line-Wrap ZWSP", "PowerPoint", "Standardizes Burmese runs to Pyidaungsu and injects word-boundary ZWSP so slide titles never wrap mid-syllable. (ဆလိုက်ခေါင်းစဉ်များ စာလုံးအလယ်မှ မပြတ်ကျစေရန် ညှိပေးသည်။)" },
                new[] { "Toolkit (ကိရိယာ)", "Kyat Lakh (ကျပ်သိန်း...တိတိ) Words Converter", "Word, Excel, PPT", "Converts numbers like 1,500,000 into official Burmese budget/banking words (auto-reads selected Excel cell!). (ဂဏန်းများကို ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ ဟု စာသားပြောင်းပေးသည်။)" },
                new[] { "Toolkit (ကိရိယာ)", "Traditional Myanmar Calendar & Sabbath (ဥပုသ်)", "Word, Excel, PPT", "Converts Gregorian dates into သာသနာသက္ကရာဇ်, မြန်မာသက္ကရာဇ်, lunar phase & Sabbath status ready to insert. (သာသနာ/မြန်မာသက္ကရာဇ်၊ လဆန်း/လဆုတ်နှင့် ဥပုသ်နေ့ တွက်ပေးသည်။)" },
                new[] { "Toolkit (ကိရိယာ)", "Official Letter Templates (ရုံးစာ ပုံစံကြမ်း)", "Word", "Inserts standard Burmese official letter headers/closings with auto-filled Myanmar calendar date. (မြန်မာရက်စွဲပါ ရုံးစာပုံစံကြမ်း ထည့်သွင်းပေးသည်။)" },
                new[] { "Custom (မှတ်သားစာ)", "Shared Personal & Organization Dictionary", "Word, Excel, PPT", "Add personal names, brand terms, or local places once — immediately shared across Word, Excel & PowerPoint. (ကိုယ်ပိုင်အမည်နှင့် ရုံးသုံးစာလုံးများ မှတ်သားထားနိုင်သည်။)" },
                new[] { "Header & Ribbon", "Eco Mode (Auto / On / Off), Slim UI & User Guide", "Word, Excel, PPT", "Controls CPU/RAM footprint for low-spec laptops, compacts header height, and opens offline HTML/Word guides. (စက်မလေးစေသော Eco စနစ်နှင့် Offline လမ်းညွှန်စာအုပ် ပါဝင်သည်။)" }
            });
            ch2.Subsections.Add(ch2s1);
            chapters.Add(ch2);

            // ==================== CHAPTER 3: COLOR LEGEND, 3 MODES & SAFE FIXING ====================
            var ch3 = new GuideChapter
            {
                Id = "proofing-guide",
                Number = "03",
                TitleEn = "How to Use Proofing: Underline Colors, 3 Modes & Safe Fixing",
                TitleMm = "စာစစ်ဆေးနည်း — မျဉ်းလှိုင်းအရောင် (၃) မျိုး၊ အရေးအသားစနစ် (၃) မျိုးနှင့် ပြင်ဆင်နည်း",
                SummaryEn = "Understand what Red, Blue, and Amber wavy underlines mean, when to pick Casual vs Standard vs Official mode, and how the Review Needed lock protects your meaning.",
                SummaryMm = "အနီ၊ အပြာ၊ လိမ္မော် မျဉ်းလှိုင်းတို့၏ အဓိပ္ပာယ်၊ Casual / Standard / Official စနစ် (၃) မျိုး ကွာခြားချက်နှင့် အမှားများကို အန္တရာယ်ကင်းစွာ ပြင်ဆင်နည်း။"
            };
            var fig3 = new GuideFigure
            {
                FigureIndex = 3,
                ImageFileName = "fig03-review-and-fix-studio.png",
                TitleEn = "Modeless Review & Fix Checklist Studio & Two-Tier Safety Lock",
                TitleMm = "စာတမ်းမပိတ်ဘဲ စိတ်ကြိုက်ရွေးချယ် ပြင်ဆင်နိုင်သော Review & Fix စိစစ်ရေးပြကွက်",
                CaptionEn = "Unlike blind auto-correctors, high-certainty spelling/encoding fixes are pre-checked (☑) while contextual warnings stay unchecked (☐ Review Needed) until you confirm.",
                CaptionMm = "သေချာသော သတ်ပုံအမှားများကိုသာ အလိုအလျောက် အမှန်ခြစ်ပေးထားပြီး အနက်သံသယရှိသော စာလုံးများကို အဝါရောင် Review Needed ဖြင့် ချန်လှပ်ကာကွယ်ထားပုံ။"
            };
            fig3.Callouts.Add(new GuideCallout { Number = 1, LabelEn = "Modeless Window Controls", ExplainEn = "Pin on top, collapse into a slim [▴ Mini] bar while typing in Office, [⇥ Dock] into the Task Pane, or export [≡ Report ▾].", LabelMm = "ပြကွက်ထိန်းချုပ်မှု ခလုတ်များ", ExplainMm = "အပေါ်ဆုံး၌ ပင်ထိုးထားရန်၊ [▴ ခေါက်] ဖြင့် ဘားအသေးချုံ့ထားရန်၊ ညာဘက်ပြကွက်သို့ ပို့ရန်နှင့် အစီရင်ခံစာထုတ်ရန်။" });
            fig3.Callouts.Add(new GuideCallout { Number = 2, LabelEn = "Select Sure Only & Group Repeated", ExplainEn = "Click [Select Sure Only] to check only 100% deterministic fixes, or group identical typos across 100+ pages into a single row.", LabelMm = "သေချာသည်များသာ ရွေးရန်နှင့် စာလုံးတူ စုစည်းခလုတ်", ExplainMm = "၁၀၀% သေချာသော အမှားများကိုသာ ရွေးရန်နှင့် စာမျက်နှာအနှံ့ ထပ်နေသော စာလုံးမှားများကို တစ်တန်းတည်း စုစည်းပြင်ရန်။" });
            fig3.Callouts.Add(new GuideCallout { Number = 3, LabelEn = "Virtualized Checklist Grid", ExplainEn = "Confirmed spelling/lookalike rows start checked (☑); yellow 'Review Needed' context/name rows stay safely unchecked (☐).", LabelMm = "အမှန်ခြစ် စိစစ်ရေးဇယားကွက်", ExplainMm = "သတ်ပုံအမှားအစစ်များကို (☑) ရွေးထားပြီး စိစစ်ရန်လိုသော အဝါရောင်တန်းများကို (☐) အမှန်ခြစ်မပါဘဲ ဘေးကင်းစွာ ပြသထားသည်။" });
            fig3.Callouts.Add(new GuideCallout { Number = 4, LabelEn = "Selected Row Inspector & Custom Edit", ExplainEn = "Inspect context, click [✓ Fix Now], [Ignore], [+ Add Word], [▸ Jump], or type any custom replacement in [✎ Custom].", LabelMm = "အသေးစိတ် စစ်ဆေးချက်နှင့် စိတ်ကြိုက်ပြင်ဆင်မှု", ExplainMm = "ရွေးထားသောအမှား၏ ရှေ့နောက်စာသားကို ကြည့်၍ ချက်ချင်းပြင်နိုင်သကဲ့သို့ [✎ Custom] တွင် စိတ်ကြိုက်စာသား ရိုက်ထည့်နိုင်သည်။" });
            fig3.Callouts.Add(new GuideCallout { Number = 5, LabelEn = "Single-Undo Batch Apply", ExplainEn = "Applies all checked replacements across the document, sheet, or slide deck in one atomic Ctrl+Z / ↶ Undo step.", LabelMm = "တစ်ချက်တည်း အတည်ပြုပြင်ဆင်ခလုတ်", ExplainMm = "ရွေးချယ်ထားသမျှကို တစ်ပြိုင်နက် ပြင်ဆင်ပေးပြီး လိုအပ်ပါက Ctrl+Z / Undo တစ်ချက်တည်းဖြင့် မူလအတိုင်း ပြန်ဆုတ်နိုင်သည်။" });
            ch3.Figures.Add(fig3);

            var ch3s1 = new GuideSubsection
            {
                HeadingEn = "3.1 Color-Coded Wavy Underline Legend (Red / Blue / Amber)",
                HeadingMm = "၃.၁ မျဉ်းလှိုင်းအရောင် (၃) မျိုး၏ အဓိပ္ပာယ်နှင့် Auto-Fix လုံခြုံရေးစနစ်",
                TableHeaders = new[] { "Underline Color", "Category (အမျိုးအစား)", "Real Examples (ဥပမာများ)", "Batch Fix Behavior" }
            };
            ch3s1.TableRows.AddRange(new[]
            {
                new[] { "Red Wavy (အနီရောင်)", "Spelling & High-Certainty Homophone (သတ်ပုံအမှား)", "ပီးပီ ➔ ပြီးပြီ, ဥက္ကဌ ➔ ဥက္ကဋ္ဌ, တ္ကသိုလ် ➔ တက္ကသိုလ်, စာတိုက်ကုတ် ➔ ကုဒ်", "✓ Auto-Checked (Safe for 1-click Fix All)" },
                new[] { "Blue Wavy (အပြာရောင်)", "Grammar, Classifier & Register Style (သဒ္ဒါနှင့် စာဟန်)", "တခု ➔ တစ်ခု, မသိပဲ ➔ မသိဘဲ, ဖိနပ် တစ်ခု ➔ တစ်ရံ, ပါတယ် ➔ ပါသည်", "✓ Auto-Checked (Safe for 1-click Fix All)" },
                new[] { "Amber Wavy (လိမ္မော်ရောင်)", "Contextual Meaning, Intruder Word & SOV Order (အနက်သံသယ / စိစစ်ရန်)", "ဥပဒေ ချိုးဖဲ့ ➔ ချိုးဖောက်, လုပ်ငန်းမျာအား ➔ လုပ်ငန်းများအား", "☐ Unchecked by Default ('Review Needed' — human confirms first!)" }
            });
            ch3.Subsections.Add(ch3s1);

            var ch3s2 = new GuideSubsection
            {
                HeadingEn = "3.2 Choosing Between the 3 Writing Modes (Casual | Standard | Official)",
                HeadingMm = "၃.၂ အရေးအသားစနစ် (၃) မျိုး (Casual | Standard | Official) ရွေးချယ်နည်း",
                TableHeaders = new[] { "Mode (စနစ်)", "Best For (သင့်တော်သော စာများ)", "Allowed Naturally (ခွင့်ပြုချက်)", "Flagged as Issues (စစ်ဆေးပြင်ဆင်ပေးမည့် အရာများ)" }
            };
            ch3s2.TableRows.AddRange(new[]
            {
                new[] { "Casual / Chat (ပေါ့ပါးဟန်)", "Social media, captions, informal notes, chat scripts", "ကျနော်, တခု, ဒါပေမယ့်, ပြဿနာ, spoken particles (တယ်/နဲ့/တွေ)", "Real spelling errors (ပီးပီ ➔ ပြီးပြီ, ဥက္ကဌ ➔ ဥက္ကဋ္ဌ) & wrong homophones" },
                new[] { "General Standard (အထွေထွေစံ)", "Articles, emails, business campaigns, speeches, textbooks", "Polite spoken particles (ပါတယ်, မယ်, တွေ, နဲ့, ဘူး)", "All 462-page သတ်ပုံကျမ်း spelling, တ/တစ် rules, ပဲ/ဘဲ, လဲ/လည်း, classifiers & majority-register clashes" },
                new[] { "Official / Literary (ရုံးသုံးစာပေ)", "Ministry letters (ရုံးစာ), academic theses, contracts, laws", "Formal literary Burmese (သည်, ၏, ၌, နှင့်, များ, သို့သော်လည်း)", "Everything in Standard PLUS all colloquial particles (ပါတယ် ➔ ပါသည်, တွေ ➔ များ, နဲ့ ➔ နှင့်)" }
            });
            ch3.Subsections.Add(ch3s2);
            chapters.Add(ch3);

            // ==================== CHAPTER 4: WORD, EXCEL & POWERPOINT WORKFLOWS ====================
            var ch4 = new GuideChapter
            {
                Id = "office-hosts",
                Number = "04",
                TitleEn = "Host-by-Host Guide: Microsoft Word, Excel & PowerPoint",
                TitleMm = "Word၊ Excel နှင့် PowerPoint တစ်ခုချင်းစီအတွက် အထူးလုပ်ဆောင်ချက်များ",
                SummaryEn = "Each Office host uses a tailored, document-safe architecture so your formatting, Excel formulas, and PowerPoint animations are never harmed.",
                SummaryMm = "Word ခေါင်းစဉ်မျဉ်းများ၊ Excel Formula တွက်ချက်မှုများနှင့် PowerPoint ဒီဇိုင်း/Animation များ လုံးဝမပျက်စီးစေရန် သီးသန့်တည်ဆောက်ထားပုံ။"
            };
            var fig4 = new GuideFigure
            {
                FigureIndex = 4,
                ImageFileName = "fig04-excel-formula-safe-proofing.png",
                TitleEn = "Microsoft Excel — 100% Formula-Safe Screen Overlay & Repeated-Typo Batch Fix",
                TitleMm = "Microsoft Excel — Formula မပျက်စီးသော ဖန်သားပြင် မျဉ်းလှိုင်းစနစ်နှင့် စာလုံးတူ အမြန်ပြင်ဆင်မှု",
                CaptionEn = "Plain-text Burmese cells display screen-only Red/Blue/Amber wavy underlines without altering .xlsx cell colors, while formula and numeric cells are 100% shielded.",
                CaptionMm = "Formula နှင့် ကိန်းဂဏန်းအကွက်များကို အပြည့်အဝ ကာကွယ်ထားပြီး စာသားအကွက်များတွင် မူရင်းဖိုင်အရောင် မပျက်စေဘဲ ဖန်သားပြင်ပေါ်၌သာ မျဉ်းလှိုင်းပြသစစ်ဆေးပုံ။"
            };
            fig4.Callouts.Add(new GuideCallout { Number = 1, LabelEn = "Screen-Only Cell Wavy Underlines", ExplainEn = "Drawn via a transparent click-through Win32 overlay — cell font color, borders, and saved .xlsx formatting remain 100% untouched.", LabelMm = "ဖိုင်အရောင်မပြောင်းသော မျဉ်းလှိုင်းစနစ်", ExplainMm = "Cell စာလုံးအရောင်ကို လုံးဝမပြောင်းဘဲ ဖန်သားပြင်ပေါ်၌သာ ပြသသဖြင့် အခြားစက်တွင်ဖွင့်လျှင်လည်း မူရင်းအတိုင်း သန့်ရှင်းနေပါသည်။" });
            fig4.Callouts.Add(new GuideCallout { Number = 2, LabelEn = "100% Formula & Accounting Shield", ExplainEn = "Cells with =SUM(...), =VLOOKUP(...), =IF(...), currencies, dates, and accounting numbers are strictly skipped and never modified.", LabelMm = "Formula နှင့် ငွေစာရင်းဂဏန်း ကာကွယ်မှု", ExplainMm = "=SUM(...), =IF(...) စသည့် Formula များနှင့် ငွေကြေးဂဏန်းအကွက်များကို လုံးဝမထိခိုက်အောင် အလိုအလျောက် ကာကွယ်ထားသည်။" });
            fig4.Callouts.Add(new GuideCallout { Number = 3, LabelEn = "Scan Scope & 25-Step Undo (↶)", ExplainEn = "Scan Active Sheet, Selection, or Entire Workbook with non-blocking 250×20 background streaming and 25-level cell Undo.", LabelMm = "Sheet / Workbook ရွေးခလုတ်နှင့် ၂၅ ဆင့် Undo", ExplainMm = "လက်ရှိ Sheet သို့မဟုတ် Workbook တစ်ခုလုံးကို စက်မထစ်ဘဲ စစ်ဆေးနိုင်ပြီး ၂၅ ဆင့်အထိ Undo ပြန်ဆုတ်နိုင်သည်။" });
            fig4.Callouts.Add(new GuideCallout { Number = 4, LabelEn = "Cell-Jump Proofing Cards (Sheet!B4)", ExplainEn = "Click any card in the Excel Task Pane to jump straight to that exact worksheet and cell coordinate.", LabelMm = "အကွက်အတိအကျ ညွှန်ပြသော ကတ်ပြားများ", ExplainMm = "ကတ်ပြားပေါ် နှိပ်လိုက်ရုံဖြင့် သက်ဆိုင်ရာ Sheet နှင့် Cell အကွက်သို့ တိုက်ရိုက်ရောက်ရှိသွားပါသည်။" });
            ch4.Figures.Add(fig4);

            var fig5 = new GuideFigure
            {
                FigureIndex = 5,
                ImageFileName = "fig05-powerpoint-slide-proofing.png",
                TitleEn = "Microsoft PowerPoint — Zero-Mutation Slide, Table & Speaker Notes Proofing",
                TitleMm = "Microsoft PowerPoint — ဒီဇိုင်းနှင့် Animation မပျက်စေသော ဆလိုက်နှင့် မှတ်စု စစ်ဆေးရေးစနစ်",
                CaptionEn = "Scans Slide Titles, TextBoxes, Tables, Grouped Shapes, and Speaker Notes with automatic Slide Show (F5) pause and formatting-preserving character edits.",
                CaptionMm = "ဆလိုက်ခေါင်းစဉ်၊ စာသားကွက်၊ ဇယား၊ Group ပုံများနှင့် Speaker Notes အားလုံးကို ဒီဇိုင်းမပျက် စစ်ဆေးပေးပြီး F5 ပြသချိန်တွင် အလိုအလျောက် ဖျောက်ထားပေးပုံ။"
            };
            fig5.Callouts.Add(new GuideCallout { Number = 1, LabelEn = "Zero-Mutation Slide Overlay", ExplainEn = "Draws Red/Blue/Amber squiggles above slide shapes without touching .pptx font colors, theme styles, or slide animations.", LabelMm = "ဒီဇိုင်းမထိခိုက်သော ဆလိုက်မျဉ်းလှိုင်း", ExplainMm = ".pptx ဖောင့်အရောင်၊ Theme နှင့် Animation များကို လုံးဝမပြောင်းလဲဘဲ အပေါ်ယံအလွှာဖြင့်သာ အမှားပြသပေးသည်။" });
            fig5.Callouts.Add(new GuideCallout { Number = 2, LabelEn = "Speaker Notes & Slide Master Coverage", ExplainEn = "Checks Presenter Speaker Notes below the slide and optional [☑ Master] layouts alongside body slides.", LabelMm = "Speaker Notes နှင့် Slide Master စစ်ဆေးမှု", ExplainMm = "ဆလိုက်အောက်ခြေရှိ ဟောပြောသူမှတ်စု (Notes) နှင့် လိုအပ်ပါက Slide Master များကိုပါ စစ်ဆေးပေးသည်။" });
            fig5.Callouts.Add(new GuideCallout { Number = 3, LabelEn = "Automatic Slide Show (F5) Shield", ExplainEn = "All overlays and popups instantly suspend whenever Slide Show or Presenter View starts.", LabelMm = "Slide Show (F5) အလိုအလျောက် ကာကွယ်မှု", ExplainMm = "F5 နှိပ်၍ Presentation ပြသချိန်တွင် မျဉ်းလှိုင်းနှင့် Popup အားလုံး အလိုအလျောက် ပျောက်ကွယ်ပေးသည်။" });
            fig5.Callouts.Add(new GuideCallout { Number = 4, LabelEn = "Slide/Shape Cards & 30-Step Undo", ExplainEn = "Jump to any slide/shape/note in 1 click and apply surgical TextRange edits backed by a 30-step deck Undo stack.", LabelMm = "ဆလိုက်အလိုက် ကတ်ပြားနှင့် ၃၀ ဆင့် Undo", ExplainMm = "သက်ဆိုင်ရာ ဆလိုက်သို့ ချက်ချင်းခုန်သွားနိုင်ပြီး ၃၀ ဆင့်အထိ မူလအတိုင်း ပြန်ဆုတ်နိုင်သည်။" });
            ch4.Figures.Add(fig5);

            var ch4s1 = new GuideSubsection
            {
                HeadingEn = "4.1 Microsoft Word — Mixed Burmese+English Shield & Safe Save/Print",
                HeadingMm = "၄.၁ Microsoft Word — မြန်မာ+အင်္ဂလိပ် ရောနှောစာတမ်း ကာကွယ်မှုနှင့် Save/Print စနစ်"
            };
            ch4s1.Bullets.Add((
                "Full Multi-Story Coverage: Scans Main Body, Tables, Headers, Footers, Footnotes, Endnotes, and Floating TextBoxes.",
                "စာကိုယ် (Body)၊ ဇယားကွက် (Tables)၊ Header/Footer၊ အောက်ခြေမှတ်စုနှင့် TextBox အားလုံးကို စစ်ဆေးပေးပါသည်။"
            ));
            ch4s1.Bullets.Add((
                "Mixed Myanmar + English Smart Shield: Automatically marks Burmese script spans with NoProofing=1 so Word's US-English spellchecker never underlines valid Burmese words in red, while keeping English words checked in English and preserving your intentional heading underlines!",
                "မြန်မာနှင့် အင်္ဂလိပ် ရောရေးထားသော စာပိုဒ်များတွင် မြန်မာစာသားများကို Word ၏ English Spellcheck အနီမျဉ်း မတက်စေရန် အလိုအလျောက် ကာကွယ်ပေးပြီး အင်္ဂလိပ်စာလုံးပေါင်းစစ်ဆေးမှုနှင့် ခေါင်းစဉ်မျဉ်းသားထားမှုများကို မူလအတိုင်း ထိန်းသိမ်းပေးပါသည်။"
            ));
            ch4s1.Bullets.Add((
                "Clean .docx Save & Print: Temporary proofing wavy underlines are automatically stripped right before Save (Ctrl+S) and Print (Ctrl+P), so colleagues without the add-in always receive clean documents.",
                "ဖိုင်သိမ်းဆည်းချိန် (Save) နှင့် ပရင့်ထုတ်ချိန် (Print) တို့တွင် ယာယီစစ်ဆေးရေးမျဉ်းများကို အလိုအလျောက် ဖယ်ရှားပေးသဖြင့် .docx ဖိုင်အတွင်း မျဉ်းလှိုင်းများ လုံးဝမကျန်ခဲ့ပါ။"
            ));
            ch4.Subsections.Add(ch4s1);

            var ch4s2 = new GuideSubsection
            {
                HeadingEn = "4.2 Microsoft Excel — 100% Formula Protection & Huge-Sheet Streaming",
                HeadingMm = "၄.၂ Microsoft Excel — Formula အပြည့်အဝ ကာကွယ်မှုနှင့် Sheet အကြီးကြီးများ စစ်ဆေးနည်း"
            };
            ch4s2.Bullets.Add((
                "Zero Formula or Cell-Color Mutation: Formulas (=SUM, =IF, =VLOOKUP, =XLOOKUP), numbers, dates, and cell font colors are NEVER modified. Red/Blue/Amber wavy lines are drawn on a transparent screen-only overlay above cells.",
                "Excel တွက်ချက်မှု Formula (=SUM, =IF, =VLOOKUP) များ၊ ကိန်းဂဏန်းများနှင့် Cell အရောင်များကို လုံးဝမထိခိုက်ပါ။ မျဉ်းလှိုင်းများကို ဖန်သားပြင်ပေါ်၌သာ ပြသသဖြင့် .xlsx ဖိုင်မူရင်း လုံးဝမပြောင်းလဲပါ။"
            ));
            ch4s2.Bullets.Add((
                "46,000 to 1,000,000 Cell Streaming + ⚡ Repeated Issues: Huge workbooks open in < 0.2s without freezing Excel and group identical typos across thousands of rows at the top of the Task Pane for 1-click batch fixing (with 25-step ↶ Undo).",
                "အကွက် သိန်းချီရှိသော Excel ဖိုင်ကြီးများကို စက်မထစ်ဘဲ နောက်ခံတွင် အပိုင်းလိုက် စစ်ဆေးပေးပြီး ထပ်ခါထပ်ခါ မှားနေသော စာလုံးတူများကို ⚡ Repeated Issues ဖြင့် တစ်ချက်တည်း ပြင်နိုင်ပါသည် (၂၅ ဆင့် Undo ပြန်ဆုတ်နိုင်သည်)။"
            ));
            ch4.Subsections.Add(ch4s2);

            var ch4s3 = new GuideSubsection
            {
                HeadingEn = "4.3 Microsoft PowerPoint — Zero-Mutation Overlay & Slide Show (F5) Shield",
                HeadingMm = "၄.၃ Microsoft PowerPoint — ဒီဇိုင်းမပျက် စစ်ဆေးမှုနှင့် Slide Show (F5) အလိုအလျောက် ကာကွယ်မှု"
            };
            ch4s3.Bullets.Add((
                "Deep Slide & Notes Scanning: Checks Slide Titles, Subtitles, Bullet Boxes, Tables [Row, Col], Recursively Grouped Shapes, and Speaker Notes (plus optional ☑ Master for Slide Masters & Layouts).",
                "ဆလိုက်ခေါင်းစဉ်၊ စာသားကွက်၊ ဇယား၊ Group ဖွဲ့ထားသော ပုံများနှင့် Speaker Notes အားလုံးကို စစ်ဆေးပေးပြီး လိုအပ်ပါက ☑ Master ဖွင့်၍ Slide Master ပါ စစ်ဆေးနိုင်ပါသည်။"
            ));
            ch4s3.Bullets.Add((
                "Automatic Slide Show (F5) Pause & Surgical Edits: Overlays and popups automatically hide the instant you start Slide Show (F5 / Presenter View), and fixes edit exact character spans so RGB colors, font sizes, bolding, and animations stay 100% intact.",
                "Slide Show (F5) ပြသချိန်တွင် မျဉ်းလှိုင်းနှင့် Popup အားလုံး အလိုအလျောက် ရပ်နားပေးပြီး၊ စာပြင်ရာတွင်လည်း ဒီဇိုင်းအရောင်၊ ဖောင့်အရွယ်အစားနှင့် Animation များ ၁၀၀% မူလအတိုင်း ကျန်ရှိပါသည်။"
            ));
            ch4.Subsections.Add(ch4s3);
            chapters.Add(ch4);

            // ==================== CHAPTER 5: DICTIONARY & ENCYCLOPEDIA ====================
            var ch5 = new GuideChapter
            {
                Id = "dictionary-suite",
                Number = "05",
                TitleEn = "Offline Bilingual Dictionary (EN ⇄ MM), Audio & 25,001 Encyclopedia",
                TitleMm = "အော့ဖ်လိုင်း အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန်၊ အသံထွက်နှင့် စွယ်စုံဗဟုသုတကျမ်း",
                SummaryEn = "Search 96,276 official Burmese headwords, 57,064 English-Myanmar entries, 25,001 Encyclopedia articles, 1,929 Proverbs, 9,221 Gazetteer places, and 8 Specialized Domains with zero internet.",
                SummaryMm = "အင်တာနက်လုံးဝမလိုဘဲ သတ်ပုံကျမ်း (၉၆,၂၇၆ ပုဒ်)၊ အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန် (၅၇,၀၆၄ ပုဒ်)၊ စွယ်စုံဗဟုသုတ (၂၅,၀၀၁ ပုဒ်)၊ စကားပုံ (၁,၉၂၉ ပုဒ်) နှင့် ပညာရပ်နယ်ပယ် (၈) ခုကို ရှာဖွေနိုင်ပါသည်။"
            };
            var fig6 = new GuideFigure
            {
                FigureIndex = 6,
                ImageFileName = "fig06-bilingual-dictionary-and-encyclopedia.png",
                TitleEn = "183,038-Entry Bilingual EN ⇄ MM Dictionary, Spoken Pronunciation & Encyclopedia Tab",
                TitleMm = "အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန်၊ အသံထွက် (▶ EN) နှင့် စွယ်စုံဗဟုသုတ ရှာဖွေရေးတက်ဘ်",
                CaptionEn = "Type Burmese, English, Romanized Burmese ('thatpon', 'kyaukse'), or Zawgyi to view official orthography, numbered senses, spoken sandhi/IPA, audio TTS, and corpus collocations.",
                CaptionMm = "မြန်မာ၊ အင်္ဂလိပ်၊ ရိုမန်အသံထွက် သို့မဟုတ် ဇော်ဂျီဖြင့် ရိုက်ရှာရုံဖြင့် သတ်ပုံအမှန်၊ အသံထွက်၊ အဓိပ္ပာယ်နှင့် တွဲသုံးစကားလုံးများကို ချက်ချင်းဖော်ပြပေးပုံ။"
            };
            fig6.Callouts.Add(new GuideCallout { Number = 1, LabelEn = "4-Way Instant Search & Lookup History", ExplainEn = "Supports Myanmar, English, Romanized Burmese, and pasted Zawgyi with [← / →] history and [★ Saved] bookmarks.", LabelMm = "နည်းလမ်း (၄) မျိုး ရှာဖွေရေးကွက်နှင့် ★ မှတ်သားချက်", ExplainMm = "မြန်မာ၊ အင်္ဂလိပ်၊ ရိုမန်အသံထွက်နှင့် ဇော်ဂျီ ၄ မျိုးလုံးဖြင့် ရှာနိုင်ပြီး [★ Saved] ဖြင့် သိမ်းထားနိုင်သည်။" });
            fig6.Callouts.Add(new GuideCallout { Number = 2, LabelEn = "7 Reference Category Filter Chips", ExplainEn = "Filter instantly by All, သတ်ပုံကျမ်း, အဘိဓာန်, ဗဟုသုတ Encyclopedia, 8 Specialized Domains (Geology ဘူမိဗေဒ...), Places, or Proverbs.", LabelMm = "ကဏ္ဍ (၇) မျိုး သီးသန့်စစ်ထုတ်ခလုတ်", ExplainMm = "သတ်ပုံကျမ်း၊ အဘိဓာန်၊ ဗဟုသုတ၊ ဘူမိဗေဒအပါအဝင် နယ်ပယ် ၈ ခု၊ ဒေသအမည် နှင့် စကားပုံများကို ခွဲထုတ်ကြည့်ရှုနိုင်သည်။" });
            fig6.Callouts.Add(new GuideCallout { Number = 3, LabelEn = "Numbered Multi-Sense Card & Official Badge", ExplainEn = "Displays official သတ်ပုံကျမ်း verification, syllable breakdown (ဝဏ္ဏခွဲ), POS badges, and clean numbered definitions.", LabelMm = "သတ်ပုံအတည်ပြုချက်၊ ဝဏ္ဏခွဲနှင့် အဓိပ္ပာယ်ကတ်ပြား", ExplainMm = "သတ်ပုံကျမ်း အတည်ပြုအမှတ်အသား၊ ဝဏ္ဏခွဲပုံနှင့် အဓိပ္ပာယ်ဖွင့်ဆိုချက်များကို ရှင်းလင်းစွာ ပြသပေးသည်။" });
            fig6.Callouts.Add(new GuideCallout { Number = 4, LabelEn = "Spoken Burmese Sandhi + IPA & [▶ EN] TTS", ExplainEn = "Shows natural Burmese spoken pronunciation alongside IPA and speaks English headwords aloud via offline Windows SAPI.", LabelMm = "မြန်မာ စကားပြောအသံထွက်နှင့် [▶ EN] အသံထွက်ခလုတ်", ExplainMm = "မြန်မာအသံထွက် (Sandhi + IPA) ကို ပြသပေးပြီး [▶ EN] နှိပ်၍ အင်္ဂလိပ်အသံထွက်ကို အော့ဖ်လိုင်း နားထောင်နိုင်သည်။" });
            fig6.Callouts.Add(new GuideCallout { Number = 5, LabelEn = "Real-World Collocations (▸) & Synonyms", ExplainEn = "Click any collocation or synonym chip (backed by 109,880 corpus bigrams) to pivot straight to that word.", LabelMm = "တွဲသုံးလေ့ရှိသော စကားလုံးများ (▸) နှင့် အနက်တူပုဒ်များ", ExplainMm = "အစိုးရနှင့် စာပေ corpus မှ တွဲသုံးစကားလုံးများနှင့် အနက်တူပုဒ်များကို နှိပ်၍ ဆက်စပ်ရှာဖွေနိုင်သည်။" });
            ch5.Figures.Add(fig6);

            var ch5s1 = new GuideSubsection
            {
                HeadingEn = "5.1 Smart 4-Way Search, Spoken Pronunciation & Specialized Domains",
                HeadingMm = "၅.၁ နည်းလမ်း (၄) မျိုးဖြင့် ရှာဖွေခြင်း၊ အသံထွက်နှင့် အထူးပြုပညာရပ် နယ်ပယ်များ"
            };
            ch5s1.Bullets.Add((
                "4-Way Input: Type in Myanmar Unicode ('သတ်ပုံ'), English ('negotiate'), Romanized Burmese ('mingalaba', 'thatpon', 'kyaukse'), or paste Zawgyi (auto-converted on the fly).",
                "မြန်မာယူနီကုဒ်၊ အင်္ဂလိပ်စာလုံး၊ အင်္ဂလိပ်အသံထွက် ('mingalaba', 'kyaukse') သို့မဟုတ် ဇော်ဂျီစာသား ကူးထည့်၍ အလွယ်တကူ ရှာဖွေနိုင်ပါသည်။"
            ));
            ch5s1.Bullets.Add((
                "Spoken Burmese Sandhi + Offline English Speech (▶ EN): View real Burmese spoken sandhi ('သပ်-ပုံ • that-pon', IPA '[θaʔ.bòʊɴ]') and click [▶ EN] to hear offline Windows SAPI English pronunciation.",
                "မြန်မာစကားပြော အသံထွက် (Sandhi + IPA) ကို ဖော်ပြပေးသည့်အပြင် [▶ EN] ခလုတ်နှိပ်၍ အင်္ဂလိပ်အသံထွက်ကို အော့ဖ်လိုင်း နားထောင်နိုင်ပါသည်။"
            ));
            ch5s1.Bullets.Add((
                "8 Specialized Academic & B2G Domains: Geology & Earth Sciences (ဘူမိဗေဒနှင့် မြေကမ္ဘာသိပ္ပံ), Public Administration & Civil Service, Higher Education & Universities, Legal & Statutory Laws, Banking/Tax/Finance, Medical & Health, Pali & Religious Studies, and IT/Cybersecurity.",
                "အထူးပြုနယ်ပယ် (၈) ခု — ဘူမိဗေဒနှင့် မြေကမ္ဘာသိပ္ပံ၊ ပြည်သူ့အုပ်ချုပ်ရေးနှင့် ဝန်ထမ်းရေးရာ၊ အဆင့်မြင့်ပညာရေး၊ ဥပဒေ၊ ဘဏ္ဍာရေး၊ ဆေးပညာ၊ ပါဠိ/သာသနာရေး နှင့် သတင်းအချက်အလက်နည်းပညာ ဝေါဟာရများ ပါဝင်ပါသည်။"
            ));
            ch5.Subsections.Add(ch5s1);
            chapters.Add(ch5);

            // ==================== CHAPTER 6: TOOLKIT & PRINTABLE A4 AUDIT REPORTS ====================
            var ch6 = new GuideChapter
            {
                Id = "toolkit-and-reports",
                Number = "06",
                TitleEn = "Official Typography Toolkit & Printable A4 Quality Audit Reports",
                TitleMm = "ရုံးစာကိရိယာများ (Toolkit) နှင့် ပုံနှိပ်ရန်အသင့် A4 အစီရင်ခံစာ ထုတ်ယူနည်း",
                SummaryEn = "One-click legacy font conversion, broken PDF reconstruction, financial Kyat Lakh words, Myanmar calendar, and 5-format ISO A4 audit reports.",
                SummaryMm = "WinInnwa/ဇော်ဂျီ ဖောင့်ပြောင်းခြင်း၊ PDF စာပျက်ပြင်ခြင်း၊ ကျပ်သိန်းစာသားပြောင်းခြင်း၊ မြန်မာပြက္ခဒိန် နှင့် A4 အစီရင်ခံစာ ထုတ်ယူနည်း။"
            };
            var fig7 = new GuideFigure
            {
                FigureIndex = 7,
                ImageFileName = "fig07-official-writing-toolkit.png",
                TitleEn = "Official Writing Toolkit (ရုံးစာကိရိယာ) & Printable ISO A4 Quality Audit Certificate",
                TitleMm = "ရုံးစာကိရိယာများ (Toolkit) နှင့် ပုံနှိပ်ရန်အသင့် A4 အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ",
                CaptionEn = "Convert legacy WinInnwa/Zawgyi fonts, repair broken Burmese PDF text, format numbers into Kyat Lakh words, compute Myanmar calendar dates, and print ISO A4 QA reports.",
                CaptionMm = "ဖောင့်ဟောင်းနှင့် PDF စာပျက်ပြင်ခြင်း၊ ကျပ်သိန်းစာသားပြောင်းခြင်း၊ မြန်မာသက္ကရာဇ်ပြက္ခဒိန် နှင့် A4 အရည်အသွေးအစီရင်ခံစာ ထုတ်ယူနိုင်သော ကိရိယာတက်ဘ်။"
            };
            fig7.Callouts.Add(new GuideCallout { Number = 1, LabelEn = "0–100% Quality Scorecard & Audit Export", ExplainEn = "Displays live document Grade (A+..D), syllable count, reading time, and 1-click Printable Audit Certificate.", LabelMm = "အရည်အသွေး အမှတ်ပေးကတ်နှင့် အစီရင်ခံစာ", ExplainMm = "စာတမ်း၏ အရည်အသွေးအဆင့် (A+..D) နှင့် စာလုံးရေကို တွက်ချက်ပြသပြီး ပုံနှိပ်အစီရင်ခံစာ ထုတ်ပေးသည်။" });
            fig7.Callouts.Add(new GuideCallout { Number = 2, LabelEn = "Legacy Font & Broken PDF Repair", ExplainEn = "Converts WinInnwa/Zawgyi ➔ Pyidaungsu and reconstructs shattered PDF copy-paste without harming English fonts or tables.", LabelMm = "WinInnwa/ဇော်ဂျီ နှင့် PDF စာပျက် ပြင်ဆင်စက်", ExplainMm = "အင်္ဂလိပ်ဖောင့်နှင့် ဇယားမပျက်စေဘဲ ယူနီကုဒ်ပြောင်းပေးပြီး PDF မှ ကူးယူရာ၌ ပျက်နေသော စာများကို ပြန်ဆက်ပေးသည်။" });
            fig7.Callouts.Add(new GuideCallout { Number = 3, LabelEn = "Smart Numerals (0-9 ➔ ၀-၉) & Punctuation", ExplainEn = "Converts prose digits while preserving ISO-9001, COVID-19, A4, 5G, URLs, and emails, and cleans invisible ZWSP.", LabelMm = "မြန်မာဂဏန်းပြောင်းစက်နှင့် ပုဒ်ထီး/ပုဒ်မ ညှိစက်", ExplainMm = "URL, A4, 5G စသည့် ကုဒ်များ မပျက်စေဘဲ မြန်မာဂဏန်းပြောင်းပေးကာ ပုဒ်ထီး/ပုဒ်မ အကွာအဝေး ညှိပေးသည်။" });
            fig7.Callouts.Add(new GuideCallout { Number = 4, LabelEn = "Banking Kyat Lakh & Myanmar Calendar", ExplainEn = "Turns 1,500,000 into 'ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ' and converts Gregorian dates into သာသနာ/မြန်မာသက္ကရာဇ် + Sabbath.", LabelMm = "ကျပ်သိန်း စာသားပြောင်းစက်နှင့် မြန်မာပြက္ခဒိန်", ExplainMm = "ငွေပမာဏကို ကျပ်သိန်းစာသားပြောင်းပေးပြီး အင်္ဂလိပ်ရက်စွဲမှ မြန်မာသက္ကရာဇ်နှင့် ဥပုသ်နေ့ တွက်ချက်ပေးသည်။" });
            ch6.Figures.Add(fig7);

            var ch6s1 = new GuideSubsection
            {
                HeadingEn = "6.1 Exporting the 5-Format Proofing & Quality Audit Report (≡ Report ▾)",
                HeadingMm = "၆.၁ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (၅) မျိုး ထုတ်ယူနည်း (≡ Report ▾)",
                TableHeaders = new[] { "Report Format", "Best Use Case", "Key Features Included" }
            };
            ch6s1.TableRows.AddRange(new[]
            {
                new[] { "Printable A4 Word (.docx) — Recommended", "Printing on A4 paper, signing & filing official QA records", "True ISO A4 Portrait (210×297 mm), Quality Grade (A+..D), Per-Page/Sheet/Slide table, Confirmed Errors vs Review Needed, Repeating table headers, Page X of Y & Sign-Off box" },
                new[] { "Multi-Sheet Excel (.xlsx)", "Sorting/filtering large audit tables across sheets", "4 dedicated sheets: Summary, Confirmed Wrong, Suspicious Review, and Fixed in Session" },
                new[] { "Executive PowerPoint (.pptx)", "Briefing management or QA meetings", "4 widescreen 16:9 executive summary slides with KPI cards & top findings" },
                new[] { "UTF-8 CSV (.csv)", "Importing into Python, R, Power BI, or databases", "UTF-8 BOM spreadsheet covering every audited finding with location & suggestion" },
                new[] { "Interactive HTML / Save as PDF", "Sharing standalone clickable report or saving PDF", "Zero-dependency browser report with live search & ⎙ Print / Save as PDF" }
            });
            ch6.Subsections.Add(ch6s1);
            chapters.Add(ch6);

            // ==================== CHAPTER 7: HOW IT WORKS UNDER THE HOOD ====================
            var ch7 = new GuideChapter
            {
                Id = "how-it-works",
                Number = "07",
                TitleEn = "How It Works Under the Hood: Hybrid NLP Architecture & Safety Engine",
                TitleMm = "စနစ်အလုပ်လုပ်ပုံ နည်းပညာဗိသုကာ (Hybrid Statistical + Symbolic Engine)",
                SummaryEn = "A transparent look inside the 6-stage offline Burmese NLP pipeline combining official 462-page orthography rules with statistical beam search.",
                SummaryMm = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း စည်းမျဉ်းများနှင့် ခေတ်မီ Statistical Language Model တို့ကို ပေါင်းစပ်ထားသော အဆင့် (၆) ဆင့် စစ်ဆေးရေးနည်းပညာ။"
            };
            var ch7s1 = new GuideSubsection
            {
                HeadingEn = "7.1 The 6-Stage Real-Time Proofing Pipeline",
                HeadingMm = "၇.၁ အဆင့် (၆) ဆင့် အချိန်နှင့်တစ်ပြေးညီ မြန်မာစာ စစ်ဆေးရေး လုပ်ငန်းစဉ်",
                TableHeaders = new[] { "Stage", "Component", "How It Works & Why It Matters" }
            };
            ch7s1.TableRows.AddRange(new[]
            {
                new[] { "Stage 1", "Canonical Unicode & O(N) Syllable FSM", "Normalizes diacritic order (U+1031/U+103A/U+1037), detects Zawgyi/WinInnwa/garbled PDF encoding, trims mid-word ZWSP surgically, and segments syllables in ~0.02 ms." },
                new[] { "Stage 2", "AES-256 + HMAC-SHA256 Binary Trie", "Loads 96,276 active headwords and 109,880 open-addressed O(1) word bigrams into a compact Trie while keeping 83,994 bilingual/encyclopedia definitions lazily hydrated on disk." },
                new[] { "Stage 3", "K=3 Beam Viterbi + Bigram LM + 45-Bit Pruner", "Segments unspaced Burmese text using a Hypothesis-Recombining Beam Viterbi lattice scored by an interpolated Witten-Bell Bigram/Unigram Language Model and 45-bit consonant masks." },
                new[] { "Stage 4", "Two-Tier Confusion Sets, Intruder & SOV Parser", "Catches real-word homophones (လျော်/လျှော်, ပစ်မှု/ပြစ်မှု, ကုတ်/ကုဒ်), dropped-geminate pat-sint typos (တ္ကသိုလ် ➔ တက္ကသိုလ်), and cue-gated proper noun shields." },
                new[] { "Stage 5", "Host-Safe Non-Destructive Rendering", "Uses live Word.Range bindings + surgical Burmese-span NoProofing=1 in Word, and per-pixel alpha click-through Win32 layered overlays in Excel & PowerPoint so .xlsx/.pptx files are never mutated." },
                new[] { "Stage 6", "EcoPerformanceGovernor Time-Slicing", "Adapts chunk sizes and caches to CPU/RAM hardware (< 35 MB core proofing heap, p50 ~ 1.6 ms per sentence) so Office stays responsive even on 4 GB RAM laptops." }
            });
            ch7.Subsections.Add(ch7s1);
            chapters.Add(ch7);

            // ==================== CHAPTER 8: TROUBLESHOOTING, FAQ & CREDITS ====================
            var ch8 = new GuideChapter
            {
                Id = "troubleshooting",
                Number = "08",
                TitleEn = "Troubleshooting, Multi-PC Tips & Author Attribution",
                TitleMm = "ပြဿနာဖြေရှင်းနည်းများ (FAQ) နှင့် ဖန်တီးသူ ဂုဏ်ပြုမှတ်တမ်း",
                SummaryEn = "Quick answers for installing on another PC, Protected View files, COM Add-ins settings, live Myanmar UI mode, and open-source GPLv3 attribution.",
                SummaryMm = "အခြားကွန်ပျူတာတွင် သွင်းယူရာ၌ ကြုံတတ်သော အချက်များ ဖြေရှင်းနည်း၊ မြန်မာဘာသာ UI စနစ်နှင့် ဖန်တီးသူ ဂုဏ်ပြုမှတ်တမ်း။"
            };
            var fig8 = new GuideFigure
            {
                FigureIndex = 8,
                ImageFileName = "fig08-bilingual-myanmar-ui-and-about.png",
                TitleEn = "Live Myanmar UI Mode (မြန်မာဘာသာ), Shared Custom Words & Open-Source Attribution Dialog",
                TitleMm = "မြန်မာဘာသာ UI မြင်ကွင်း၊ ကိုယ်ပိုင်မှတ်သားစာလုံးများနှင့် Open-Source လိုင်စင်/ဖန်တီးသူ ပြကွက်",
                CaptionEn = "Switch the entire interface into Burmese with 1 click, manage shared Custom Words across Word/Excel/PowerPoint, and open offline HTML/A4 manuals anytime from the About dialog.",
                CaptionMm = "ခလုတ်တစ်ချက်ဖြင့် မြန်မာဘာသာ UI သို့ ပြောင်းလဲအသုံးပြုပုံ၊ ရုံးသုံး/ကိုယ်ပိုင်စာလုံး မှတ်သားပုံနှင့် ဖန်တီးသူ ဂုဏ်ပြုမှတ်တမ်း ပြကွက်။"
            };
            fig8.Callouts.Add(new GuideCallout { Number = 1, LabelEn = "100% Live Myanmar UI Mode (က/A မြန်မာ)", ExplainEn = "Every tab, button, tooltip, and status message switches immediately between English and Myanmar with zero Office restart.", LabelMm = "၁၀၀% မြန်မာဘာသာ စနစ်မျက်နှာပြင်", ExplainMm = "တက်ဘ်၊ ခလုတ်နှင့် ရှင်းလင်းချက်အားလုံးကို မြန်မာဘာသာဖြင့် တိုက်ရိုက်ပြောင်းလဲ သုံးစွဲနိုင်သည်။" });
            fig8.Callouts.Add(new GuideCallout { Number = 2, LabelEn = "Shared Custom Dictionary (မှတ်သားစာ)", ExplainEn = "Add personal names, university departments, or ministry acronyms once — instantly recognized across Word, Excel & PowerPoint.", LabelMm = "Word • Excel • PPT ဘုံသုံး ကိုယ်ပိုင်အဘိဓာန်", ExplainMm = "လူအမည်၊ ဌာနအမည်များကို တစ်ကြိမ်ထည့်သွင်းရုံဖြင့် Office သုံးမျိုးလုံးတွင် တစ်ပြိုင်နက် သိရှိမှတ်သားပေးသည်။" });
            fig8.Callouts.Add(new GuideCallout { Number = 3, LabelEn = "100% Free Offline Open-Source Status", ExplainEn = "Confirms all 183,038 lexicon & encyclopedia entries are active offline with no .lic activation key required.", LabelMm = "လိုင်စင်ဖိုင်မလိုသော ၁၀၀% အခမဲ့ အော့ဖ်လိုင်းစနစ်", ExplainMm = "အင်တာနက်နှင့် .lic လိုင်စင်ဖိုင် လုံးဝမလိုဘဲ ဝေါဟာရ ၁၈၃,၀၃၈ ပုဒ်လုံး အသင့်အလုပ်လုပ်နေကြောင်း ပြသပေးသည်။" });
            fig8.Callouts.Add(new GuideCallout { Number = 4, LabelEn = "Author Attribution & Offline Guide Launchers", ExplainEn = "Preserves GPLv3 Section 7(b) credits (Htet Yar Zar — Kyaukse University) and opens the HTML or A4 Word User Guide offline.", LabelMm = "ဖန်တီးသူ ဂုဏ်ပြုမှတ်တမ်းနှင့် လမ်းညွှန်စာအုပ် ခလုတ်များ", ExplainMm = "ဖန်တီးသူ ထက်ရာဇာ (ကျောက်ဆည်တက္ကသိုလ်) မှတ်တမ်းနှင့် HTML / Word A4 လမ်းညွှန်စာအုပ် အမြန်ဖွင့်ခလုတ်များ ပါဝင်သည်။" });
            ch8.Figures.Add(fig8);

            var ch8s1 = new GuideSubsection
            {
                HeadingEn = "8.1 Common Questions & Instant Fixes",
                HeadingMm = "၈.၁ အမေးများသော ပြဿနာများနှင့် အမြန်ဖြေရှင်းနည်း",
                TableHeaders = new[] { "Symptom (ကြုံတွေ့ရသော အခြေအနေ)", "Cause (အကြောင်းရင်း)", "1-Step Solution (ဖြေရှင်းနည်း)" }
            };
            ch8s1.TableRows.AddRange(new[]
            {
                new[] { "PowerPoint/Excel downloaded file won't scan", "File opened in Office 'Protected View' (Read-Only) from Internet/Telegram/Email", "Click 'Enable Editing' on the yellow bar at the top of Office, then click Scan. (အပေါ်ဘက် အဝါရောင်တန်းရှိ Enable Editing ကို အရင်နှိပ်ပါ။)" },
                new[] { "'Myanmar Spell Checker' tab not visible on Ribbon", "Add-in unchecked in Office COM Add-ins or Office was open during install", "Go to File ➔ Options ➔ Add-ins ➔ Manage: COM Add-ins ➔ Go..., check 'Myanmar Spell Checker', and click OK (or re-run Install-EasyUser.bat with Office closed)." },
                new[] { "Installer says 'Extract ZIP First'", "Double-clicked .bat inside Windows Explorer ZIP preview without extracting", "Right-click the .zip file ➔ 'Extract All...' ➔ run Install-MyanmarSpellChecker.bat / Install-EasyUser.bat from the extracted folder." },
                new[] { "Valid Burmese words show red lines in a mixed English+Myanmar Word file", "Word's native US-English spellchecker ran before initial scan", "Click 'Scan Doc' or 'Clear Marks' once — Burmese spans get NoProofing=1 automatically while English typos stay checked!" },
                new[] { "Myanmar letters look like squares or overlapping circles", "PC does not have Pyidaungsu Unicode font installed", "Install the standard Pyidaungsu font on Windows, and use Toolkit ➔ 'WinInnwa/Zawgyi ➔ Pyidaungsu' if the file uses legacy fonts." }
            });
            ch8.Subsections.Add(ch8s1);

            var ch8s2 = new GuideSubsection
            {
                HeadingEn = "8.2 Creator, Special Dedication, Collaborators & GNU GPL v3.0 License",
                HeadingMm = "၈.၂ ဖန်တီးသူ၊ အထူးဂုဏ်ပြုမှတ်တမ်း၊ ပူးပေါင်းပါဝင်သူများနှင့် GNU GPL v3.0 လိုင်စင်"
            };
            ch8s2.Bullets.Add((
                "Original Creator & Lead Developer: Htet Yar Zar (ထက်ရာဇာ) — M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University, Kyaukse, Mandalay Region, Myanmar (https://github.com/Hashyz/MyanmarSpellChecker).",
                "မူလဖန်တီးသူနှင့် ဦးဆောင်ရေးသားသူ — ထက်ရာဇာ (Htet Yar Zar)၊ မဟာသိပ္ပံ ဒုတိယနှစ် [M.Sc. Part II] ဘူမိဗေဒ အထူးပြု (M.Sc. Part I အောင်မြင်ပြီး)၊ ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ်၊ ကျောက်ဆည်မြို့၊ မန္တလေးတိုင်းဒေသကြီး။"
            ));
            ch8s2.Bullets.Add((
                "Special Dedication (မူလစိတ်ကူးလမ်းညွှန်ပေးသူ): U Moe Kyaw Thu (ဦးမိုးကျော်သူ) — Beloved Father of Htet Yar Zar, who originally inspired and gave the idea to create this Myanmar Spell & Grammar Suite.",
                "အထူးဂုဏ်ပြုမှတ်တမ်း — ဤမြန်မာစာ စစ်ဆေးရေးစနစ် ဖန်တီးပြုစုရန် မူလစိတ်ကူးနှင့် လမ်းညွှန်မှုပေးခဲ့သူ ကျေးဇူးရှင် ဖခင်ကြီး ဦးမိုးကျော်သူ (U Moe Kyaw Thu)။"
            ));
            ch8s2.Bullets.Add((
                "Project Collaborators (ပူးပေါင်းပါဝင်သူများ): Hein Htet Zaw (ဟိန်းထက်ဇော်) & Thu Reain Khant (သူရိန်ခန့်).",
                "ပူးပေါင်းပါဝင်သူများ — ဟိန်းထက်ဇော် (Hein Htet Zaw) နှင့် သူရိန်ခန့် (Thu Reain Khant)။"
            ));
            ch8s2.Bullets.Add((
                "Open-Source License: 100% Free & Open-Source under GNU General Public License v3.0 (GPL-3.0-or-later) with mandatory Section 7(b)/(c) author & origin preservation.",
                "လိုင်စင် — GNU GPL v3.0 (Section 7b/7c မူလဖန်တီးသူအမည် မပယ်ဖျက်ရ စည်းကမ်းချက်အပါအဝင်) ဖြင့် အခမဲ့ Open-Source ဖြန့်ချိထားပါသည်။"
            ));
            ch8.Subsections.Add(ch8s2);
            chapters.Add(ch8);

            return chapters;
        }

        // ==================== 1. GITHUB MARKDOWN GENERATOR ====================

        public static string BuildMarkdownUserGuide()
        {
            var chapters = BuildStructuredChapters();
            var sb = new StringBuilder(49152);

            sb.AppendLine("# Myanmar Spell Checker — Complete Illustrated User Guide & Architecture Handbook");
            sb.AppendLine("## မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး — ပုံနှင့်တကွ အသုံးပြုပုံလမ်းညွှန်အပြည့်အစုံ (Word • Excel • PowerPoint)");
            sb.AppendLine();
            sb.AppendLine("> **100% Offline Unified 3-in-1 Microsoft Office Suite** (`Word + Excel + PowerPoint`, `.NET Framework 4.8`)  ");
            sb.AppendLine("> **Author:** **Htet Yar Zar (ထက်ရာဇာ)** — Department of Geology, Kyaukse University, Kyaukse, Myanmar  ");
            sb.AppendLine("> **Special Dedication:** **U Moe Kyaw Thu (ဦးမိုးကျော်သူ)** • **Collaborators:** **Hein Htet Zaw (ဟိန်းထက်ဇော်)** & **Thu Reain Khant (သူရိန်ခန့်)**  ");
            sb.AppendLine("> **License:** GNU GPL v3.0 + Mandatory Section 7(b) Attribution (`183,038` Compiled Entries • Zero Internet Calls)");
            sb.AppendLine();
            sb.AppendLine("## Table of Contents / မာတိကာ");
            foreach (var ch in chapters)
            {
                sb.AppendLine($"- [**{ch.Number}. {ch.TitleEn}** — *{ch.TitleMm}*](#{ch.Id})");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var ch in chapters)
            {
                sb.AppendLine($"<a id=\"{ch.Id}\"></a>");
                sb.AppendLine($"## {ch.Number}. {ch.TitleEn}");
                sb.AppendLine($"### {ch.TitleMm}");
                sb.AppendLine();
                sb.AppendLine($"> **EN:** {ch.SummaryEn}  ");
                sb.AppendLine($"> **MM:** {ch.SummaryMm}");
                sb.AppendLine();

                foreach (var fig in ch.Figures)
                {
                    sb.AppendLine($"![Figure {fig.FigureIndex} — {fig.TitleEn}](images/{fig.ImageFileName})");
                    sb.AppendLine();
                    sb.AppendLine($"**Figure {fig.FigureIndex} (`ပုံ {fig.FigureIndex}`) — {fig.TitleEn} / {fig.TitleMm}**  ");
                    sb.AppendLine($"*{fig.CaptionEn}*  ");
                    sb.AppendLine($"*{fig.CaptionMm}*");
                    sb.AppendLine();
                    if (fig.Callouts.Count > 0)
                    {
                        sb.AppendLine("| # | Feature / Control (`အစိတ်အပိုင်း`) | Explanation (`ရှင်းလင်းချက် — EN / မြန်မာ`) |");
                        sb.AppendLine("| :---: | :--- | :--- |");
                        foreach (var co in fig.Callouts)
                        {
                            string badge = FormatCircledNumber(co.Number);
                            string label = $"**{co.LabelEn}**<br/>*{co.LabelMm}*".Replace("|", "\\|");
                            string desc = $"{co.ExplainEn}<br/>*{co.ExplainMm}*".Replace("|", "\\|");
                            sb.AppendLine($"| **{badge}** | {label} | {desc} |");
                        }
                        sb.AppendLine();
                    }
                }

                foreach (var sub in ch.Subsections)
                {
                    sb.AppendLine($"#### {sub.HeadingEn} / {sub.HeadingMm}");
                    sb.AppendLine();

                    if (sub.Bullets.Count > 0)
                    {
                        foreach (var (en, mm) in sub.Bullets)
                        {
                            sb.AppendLine($"- **{en}**  ");
                            sb.AppendLine($"  *{mm}*");
                        }
                        sb.AppendLine();
                    }

                    if (sub.TableHeaders != null && sub.TableHeaders.Length > 0 && sub.TableRows.Count > 0)
                    {
                        sb.AppendLine("| " + string.Join(" | ", sub.TableHeaders) + " |");
                        var sep = new string[sub.TableHeaders.Length];
                        for (int i = 0; i < sep.Length; i++) sep[i] = ":---";
                        sb.AppendLine("| " + string.Join(" | ", sep) + " |");
                        foreach (var row in sub.TableRows)
                        {
                            var escaped = new string[row.Length];
                            for (int i = 0; i < row.Length; i++)
                                escaped[i] = (row[i] ?? string.Empty).Replace("|", "\\|");
                            sb.AppendLine("| " + string.Join(" | ", escaped) + " |");
                        }
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("---");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ==================== 2. INTERACTIVE OFFLINE HTML HANDBOOK GENERATOR ====================

        public static string BuildInteractiveHtmlUserGuide(string? imagesDirectoryOverride = null)
        {
            var chapters = BuildStructuredChapters();
            string? imgDir = FindDefaultImagesDirectory(imagesDirectoryOverride);
            var sb = new StringBuilder(131072);

            sb.Append(@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>Myanmar Spell Checker — Illustrated User Guide (မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး လမ်းညွှန်)</title>
<style>
:root {
  --brand:#185ABC; --brand-dark:#0D3B82; --bg:#F4F7FC; --card:#FFFFFF;
  --ink:#1A2332; --muted:#526075; --border:#D8E1EE;
  --red:#C92A2A; --blue:#1864AB; --amber:#D97706; --green:#16794B;
}
* { box-sizing:border-box; }
body {
  margin:0; font-family:'Pyidaungsu','Myanmar Text','Segoe UI',system-ui,sans-serif;
  background:var(--bg); color:var(--ink); line-height:1.68;
}
.topbar {
  position:sticky; top:0; z-index:50; background:linear-gradient(135deg,#0F3D8A,#185ABC);
  color:#fff; padding:12px 24px; display:flex; flex-wrap:wrap; gap:12px;
  align-items:center; justify-content:space-between; box-shadow:0 2px 10px rgba(13,59,130,.18);
}
.topbar-title { font-size:1.12rem; font-weight:700; }
.topbar-sub { font-size:0.82rem; opacity:0.9; }
.toolbar { display:flex; flex-wrap:wrap; gap:8px; align-items:center; }
.search-input {
  padding:7px 12px; border-radius:6px; border:1px solid rgba(255,255,255,.35);
  background:rgba(255,255,255,.96); color:#1A2332; min-width:230px; font-family:inherit; font-size:0.9rem;
}
.pill-btn {
  padding:6px 12px; border-radius:6px; border:1px solid rgba(255,255,255,.4);
  background:rgba(255,255,255,.14); color:#fff; cursor:pointer; font-family:inherit;
  font-size:0.84rem; font-weight:600; text-decoration:none; display:inline-block;
}
.pill-btn:hover, .pill-btn.active { background:#fff; color:var(--brand-dark); }
.layout { max-width:1280px; margin:0 auto; display:grid; grid-template-columns:270px 1fr; gap:22px; padding:22px; }
.sidebar {
  position:sticky; top:76px; align-self:start; background:var(--card); border:1px solid var(--border);
  border-radius:10px; padding:14px; max-height:calc(100vh - 96px); overflow-y:auto;
}
.sidebar h3 { margin:0 0 10px 0; font-size:0.92rem; color:var(--brand-dark); text-transform:uppercase; letter-spacing:.03em; }
.nav-link {
  display:block; padding:7px 10px; border-radius:6px; color:var(--ink); text-decoration:none;
  font-size:0.88rem; margin-bottom:4px; border-left:3px solid transparent;
}
.nav-link:hover { background:#EEF4FF; border-left-color:var(--brand); color:var(--brand-dark); }
.hero {
  background:var(--card); border:1px solid var(--border); border-top:4px solid var(--brand);
  border-radius:10px; padding:20px 24px; margin-bottom:20px;
}
.kpi-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(175px,1fr)); gap:10px; margin-top:14px; }
.kpi { background:#F0F5FF; border:1px solid #CFE0FC; border-radius:8px; padding:10px 12px; }
.kpi strong { display:block; font-size:1.1rem; color:var(--brand-dark); }
.kpi span { font-size:0.82rem; color:var(--muted); }
.chapter {
  background:var(--card); border:1px solid var(--border); border-radius:10px;
  padding:20px 24px; margin-bottom:18px;
}
.ch-badge {
  display:inline-block; background:#E7F0FF; color:var(--brand-dark); font-weight:700;
  font-size:0.78rem; padding:2px 9px; border-radius:99px; margin-bottom:6px;
}
.chapter h2 { margin:2px 0 4px 0; font-size:1.25rem; color:var(--brand-dark); }
.chapter h3.mm-title { margin:0 0 10px 0; font-size:1.05rem; color:#2C3E55; font-weight:600; }
.summary-box {
  background:#F8FAFE; border-left:4px solid var(--brand); padding:10px 14px;
  border-radius:0 6px 6px 0; margin-bottom:14px; font-size:0.92rem;
}
figure.guide-fig {
  margin:16px 0 20px 0; background:#F8FAFF; border:1px solid #C9D8EE;
  border-radius:10px; overflow:hidden; box-shadow:0 3px 12px rgba(15,61,138,.07);
}
.fig-img-wrap {
  background:#E9EEF6; padding:12px; text-align:center; border-bottom:1px solid #D5E0F0; position:relative;
}
.fig-img-wrap img {
  max-width:100%; height:auto; border-radius:6px; border:1px solid #BCCCE2;
  box-shadow:0 2px 8px rgba(0,0,0,.10); cursor:zoom-in; display:block; margin:0 auto;
}
.zoom-hint {
  font-size:0.77rem; color:var(--muted); margin-top:6px;
}
figcaption.fig-cap { padding:14px 18px; background:#FFFFFF; }
.fig-cap-title { font-weight:700; color:var(--brand-dark); font-size:0.98rem; margin-bottom:4px; }
.fig-cap-desc { font-size:0.89rem; color:#334155; margin-bottom:12px; }
.callout-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(280px,1fr)); gap:9px; }
.callout-item {
  display:flex; gap:10px; align-items:flex-start; background:#F4F8FF;
  border:1px solid #DCE7F9; border-radius:7px; padding:8px 11px; font-size:0.86rem;
}
.callout-num {
  flex:0 0 25px; height:25px; border-radius:50%; background:#D93025; color:#fff;
  font-weight:700; font-size:0.82rem; display:flex; align-items:center; justify-content:center;
  box-shadow:0 1px 4px rgba(217,48,37,.35); margin-top:1px;
}
.callout-body strong { display:block; color:#0F3D8A; }
.subsec h4 { margin:16px 0 8px 0; font-size:1.02rem; color:#183B66; border-bottom:1px solid #E9EEF6; padding-bottom:4px; }
ul.guide-list { padding-left:20px; margin:8px 0 14px 0; }
ul.guide-list li { margin-bottom:9px; }
.mm-block { color:#2E4057; display:block; margin-top:2px; }
table.guide-tbl { width:100%; border-collapse:collapse; margin:10px 0 14px 0; font-size:0.89rem; }
table.guide-tbl th {
  background:#185ABC; color:#fff; text-align:left; padding:9px 10px; border:1px solid #154EA3;
}
table.guide-tbl td { padding:8px 10px; border:1px solid var(--border); vertical-align:top; }
table.guide-tbl tr:nth-child(even) td { background:#F9FBFF; }
#lightbox {
  display:none; position:fixed; inset:0; z-index:999; background:rgba(10,18,32,.88);
  align-items:center; justify-content:center; padding:24px; cursor:zoom-out;
}
#lightbox.open { display:flex; }
#lightbox img { max-width:96vw; max-height:92vh; border-radius:8px; box-shadow:0 8px 32px rgba(0,0,0,.5); background:#fff; }
body[data-lang=""en""] .lang-mm { display:none !important; }
body[data-lang=""mm""] .lang-en { display:none !important; }
footer.credits {
  text-align:center; font-size:0.84rem; color:var(--muted); padding:18px;
  border-top:1px solid var(--border); margin-top:10px;
}
@media (max-width: 900px) {
  .layout { grid-template-columns:1fr; }
  .sidebar { position:static; max-height:none; }
}
@media print {
  .topbar, .sidebar, .zoom-hint, #lightbox { display:none !important; }
  .layout { display:block; padding:0; max-width:none; }
  .chapter, .hero, figure.guide-fig { break-inside:avoid; box-shadow:none; border:1px solid #ccc; }
  body { background:#fff; font-size:10pt; }
}
</style>
</head>
<body data-lang=""both"">
<header class=""topbar"">
  <div>
    <div class=""topbar-title"">Myanmar Spell Checker — Illustrated User Guide &amp; Tool Handbook</div>
    <div class=""topbar-sub"">မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး ပုံနှင့်တကွ အသုံးပြုပုံလမ်းညွှန် (Word • Excel • PowerPoint — 100% Offline)</div>
  </div>
  <div class=""toolbar"">
    <input id=""searchBox"" class=""search-input"" type=""search"" placeholder=""Search tools, captions, FAQ... (ရှာရန်)"" oninput=""filterGuide(this.value)"" />
    <button class=""pill-btn active"" id=""btnBoth"" onclick=""setLang('both')"">EN + မြန်မာ</button>
    <button class=""pill-btn"" id=""btnEn"" onclick=""setLang('en')"">English</button>
    <button class=""pill-btn"" id=""btnMm"" onclick=""setLang('mm')"">မြန်မာ</button>
    <a class=""pill-btn"" href=""Myanmar-Spell-Checker-User-Guide.docx"">Word A4 (.docx)</a>
    <button class=""pill-btn"" onclick=""window.print()"">⎙ Print / Save PDF</button>
  </div>
</header>
<div class=""layout"">
  <nav class=""sidebar"">
    <h3>Contents / မာတိကာ</h3>
");
            foreach (var ch in chapters)
            {
                sb.Append("    <a class=\"nav-link\" href=\"#").Append(H(ch.Id)).Append("\">")
                  .Append(H(ch.Number)).Append(". ")
                  .Append("<span class=\"lang-en\">").Append(H(ch.TitleEn)).Append("</span> ")
                  .Append("<span class=\"lang-mm\">(").Append(H(ch.TitleMm)).Append(")</span></a>\n");
            }

            sb.Append(@"  </nav>
  <main id=""guideMain"">
    <section class=""hero"">
      <h2 style=""margin:0 0 6px 0;color:#0F3D8A;"">Unified 3-in-1 Offline Myanmar Office Proofing, Dictionary &amp; Official Toolkit</h2>
      <div class=""lang-mm"" style=""font-weight:600;color:#2E4057;"">Microsoft Word၊ Excel နှင့် PowerPoint သုံးမျိုးလုံးအတွက် ၁၀၀% အော့ဖ်လိုင်း မြန်မာစာလုံးပေါင်း၊ သဒ္ဒါ၊ အဘိဓာန်နှင့် ရုံးစာကိရိယာ</div>
      <div class=""kpi-grid"">
        <div class=""kpi""><strong>183,038 Entries</strong><span>96,276 Headwords + 57,064 EN⇄MM + 25,001 Encyclopedia</span></div>
        <div class=""kpi""><strong>Word • Excel • PPT</strong><span>Unified Ribbon + Review Tab + 4-Tab Task Pane</span></div>
        <div class=""kpi""><strong>8 Visual Walkthroughs</strong><span>Annotated Real-UI Screenshots + Numbered Callout Captions</span></div>
        <div class=""kpi""><strong>100% Offline &amp; Safe</strong><span>Zero Internet • Formula-Safe • Slide-Show F5 Safe</span></div>
      </div>
    </section>
");

            foreach (var ch in chapters)
            {
                sb.Append("    <section class=\"chapter\" id=\"").Append(H(ch.Id)).Append("\">\n");
                sb.Append("      <span class=\"ch-badge\">CHAPTER ").Append(H(ch.Number)).Append("</span>\n");
                sb.Append("      <h2 class=\"lang-en\">").Append(H(ch.TitleEn)).Append("</h2>\n");
                sb.Append("      <h3 class=\"mm-title lang-mm\">").Append(H(ch.TitleMm)).Append("</h3>\n");
                sb.Append("      <div class=\"summary-box\">");
                sb.Append("<div class=\"lang-en\">").Append(H(ch.SummaryEn)).Append("</div>");
                sb.Append("<div class=\"lang-mm mm-block\">").Append(H(ch.SummaryMm)).Append("</div>");
                sb.Append("</div>\n");

                foreach (var fig in ch.Figures)
                {
                    AppendHtmlFigureBlock(sb, fig, imgDir);
                }

                foreach (var sub in ch.Subsections)
                {
                    sb.Append("      <div class=\"subsec\">\n");
                    sb.Append("        <h4><span class=\"lang-en\">").Append(H(sub.HeadingEn)).Append("</span> <span class=\"lang-mm\">")
                      .Append(H(sub.HeadingMm)).Append("</span></h4>\n");

                    if (sub.Bullets.Count > 0)
                    {
                        sb.Append("        <ul class=\"guide-list\">\n");
                        foreach (var (en, mm) in sub.Bullets)
                        {
                            sb.Append("          <li><span class=\"lang-en\"><strong>").Append(H(en)).Append("</strong></span>")
                              .Append("<span class=\"lang-mm mm-block\">").Append(H(mm)).Append("</span></li>\n");
                        }
                        sb.Append("        </ul>\n");
                    }

                    if (sub.TableHeaders != null && sub.TableHeaders.Length > 0 && sub.TableRows.Count > 0)
                    {
                        sb.Append("        <table class=\"guide-tbl\"><thead><tr>");
                        foreach (string th in sub.TableHeaders)
                            sb.Append("<th>").Append(H(th)).Append("</th>");
                        sb.Append("</tr></thead><tbody>\n");
                        foreach (var row in sub.TableRows)
                        {
                            sb.Append("          <tr>");
                            foreach (string cell in row)
                                sb.Append("<td>").Append(H(cell)).Append("</td>");
                            sb.Append("</tr>\n");
                        }
                        sb.Append("        </tbody></table>\n");
                    }

                    sb.Append("      </div>\n");
                }

                sb.Append("    </section>\n");
            }

            sb.Append(@"    <footer class=""credits"">
      <strong>Myanmar Spell Checker (မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း)</strong> • Created by <strong>Htet Yar Zar (ထက်ရာဇာ)</strong>, Department of Geology, Kyaukse University, Kyaukse, Myanmar.<br/>
      Special Dedication: <strong>U Moe Kyaw Thu (ဦးမိုးကျော်သူ)</strong> • Collaborators: <strong>Hein Htet Zaw (ဟိန်းထက်ဇော်)</strong> &amp; <strong>Thu Reain Khant (သူရိန်ခန့်)</strong> • Licensed under GNU GPL v3.0 (Section 7b).
    </footer>
  </main>
</div>
<div id=""lightbox"" onclick=""closeLightbox()""><img id=""lightboxImg"" src="""" alt=""Zoomed screenshot"" /></div>
<script>
function setLang(mode) {
  document.body.setAttribute('data-lang', mode);
  document.getElementById('btnBoth').className = 'pill-btn' + (mode === 'both' ? ' active' : '');
  document.getElementById('btnEn').className = 'pill-btn' + (mode === 'en' ? ' active' : '');
  document.getElementById('btnMm').className = 'pill-btn' + (mode === 'mm' ? ' active' : '');
}
function filterGuide(q) {
  var term = (q || '').trim().toLowerCase();
  var secs = document.querySelectorAll('.chapter');
  for (var i = 0; i < secs.length; i++) {
    var txt = secs[i].innerText.toLowerCase();
    secs[i].style.display = (!term || txt.indexOf(term) >= 0) ? '' : 'none';
  }
}
function openLightbox(imgEl) {
  var lb = document.getElementById('lightbox');
  var lbImg = document.getElementById('lightboxImg');
  lbImg.src = imgEl.src;
  lb.className = 'open';
}
function closeLightbox() {
  document.getElementById('lightbox').className = '';
}
document.addEventListener('keydown', function(e) {
  if (e.key === 'Escape') closeLightbox();
});
</script>
</body>
</html>");

            return sb.ToString();
        }

        private static void AppendHtmlFigureBlock(StringBuilder sb, GuideFigure fig, string? imgDir)
        {
            string imgSrc = BuildHtmlImageSource(fig, imgDir);
            sb.Append("      <figure class=\"guide-fig\">\n");
            sb.Append("        <div class=\"fig-img-wrap\">\n");
            sb.Append("          <img src=\"").Append(imgSrc).Append("\" alt=\"Figure ").Append(fig.FigureIndex).Append(" — ").Append(H(fig.TitleEn)).Append("\" onclick=\"openLightbox(this)\" />\n");
            sb.Append("          <div class=\"zoom-hint\"><span class=\"lang-en\">Click screenshot to enlarge full-screen (Esc to close)</span> <span class=\"lang-mm\">(ပုံကို အကြီးချဲ့ကြည့်ရန် ပုံပေါ်နှိပ်ပါ)</span></div>\n");
            sb.Append("        </div>\n");
            sb.Append("        <figcaption class=\"fig-cap\">\n");
            sb.Append("          <div class=\"fig-cap-title\"><span class=\"lang-en\">Figure ").Append(fig.FigureIndex).Append(": ").Append(H(fig.TitleEn)).Append("</span> <span class=\"lang-mm\">ပုံ (").Append(fig.FigureIndex).Append(") — ").Append(H(fig.TitleMm)).Append("</span></div>\n");
            sb.Append("          <div class=\"fig-cap-desc\"><span class=\"lang-en\">").Append(H(fig.CaptionEn)).Append("</span> <span class=\"lang-mm mm-block\">").Append(H(fig.CaptionMm)).Append("</span></div>\n");
            if (fig.Callouts.Count > 0)
            {
                sb.Append("          <div class=\"callout-grid\">\n");
                foreach (var co in fig.Callouts)
                {
                    sb.Append("            <div class=\"callout-item\"><span class=\"callout-num\">").Append(co.Number).Append("</span><div class=\"callout-body\">")
                      .Append("<span class=\"lang-en\"><strong>").Append(H(co.LabelEn)).Append("</strong>").Append(H(co.ExplainEn)).Append("</span>")
                      .Append("<span class=\"lang-mm mm-block\"><strong>").Append(H(co.LabelMm)).Append(" — </strong>").Append(H(co.ExplainMm)).Append("</span>")
                      .Append("</div></div>\n");
                }
                sb.Append("          </div>\n");
            }
            sb.Append("        </figcaption>\n");
            sb.Append("      </figure>\n");
        }

        private static string BuildHtmlImageSource(GuideFigure fig, string? imgDir)
        {
            if (!string.IsNullOrEmpty(imgDir))
            {
                string fullPng = Path.Combine(imgDir!, fig.ImageFileName);
                if (File.Exists(fullPng))
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(fullPng);
                        if (bytes.Length > 64)
                            return "data:image/png;base64," + Convert.ToBase64String(bytes);
                    }
                    catch { }
                }
            }

            // Crisp self-contained SVG fallback diagram if PNG assets were not on disk
            string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='960' height='220' viewBox='0 0 960 220'>" +
                         $"<rect width='960' height='220' rx='10' fill='#EEF4FF' stroke='#185ABC' stroke-width='2'/>" +
                         $"<rect x='0' y='0' width='960' height='44' rx='8' fill='#185ABC'/>" +
                         $"<text x='20' y='28' fill='#FFFFFF' font-family='Segoe UI,sans-serif' font-size='16' font-weight='bold'>Figure {fig.FigureIndex}: {H(fig.TitleEn)}</text>" +
                         $"<text x='20' y='82' fill='#0F3D8A' font-family='Pyidaungsu,Segoe UI,sans-serif' font-size='15' font-weight='bold'>{H(fig.TitleMm)}</text>" +
                         $"<text x='20' y='118' fill='#334155' font-family='Segoe UI,sans-serif' font-size='13'>{H(fig.CaptionEn)}</text>" +
                         $"<text x='20' y='152' fill='#334155' font-family='Pyidaungsu,Segoe UI,sans-serif' font-size='13'>{H(fig.CaptionMm)}</text>" +
                         $"</svg>";
            return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        }

        // ==================== 3. PRINTABLE A4 WORD (.DOCX) GENERATOR ====================

        public static byte[] BuildPrintableA4WordUserGuideBytes(string? imagesDirectoryOverride = null)
        {
            var chapters = BuildStructuredChapters();
            string? imgDir = FindDefaultImagesDirectory(imagesDirectoryOverride);
            var embeddedImages = new Dictionary<int, (string RelId, string MediaName, byte[] Bytes, long WidthEmu, long HeightEmu)>();

            if (!string.IsNullOrEmpty(imgDir))
            {
                foreach (var ch in chapters)
                {
                    foreach (var fig in ch.Figures)
                    {
                        string pngPath = Path.Combine(imgDir!, fig.ImageFileName);
                        if (!File.Exists(pngPath)) continue;
                        try
                        {
                            byte[] pngBytes = File.ReadAllBytes(pngPath);
                            if (TryReadPngDimensions(pngBytes, out int wPx, out int hPx))
                            {
                                long cx = MaxFigureWidthEmus;
                                long cy = Math.Max(1200000L, (long)Math.Round(cx * (double)hPx / Math.Max(1, wPx)));
                                string relId = $"rIdFig{fig.FigureIndex}";
                                string mediaName = $"fig{fig.FigureIndex:D2}.png";
                                embeddedImages[fig.FigureIndex] = (relId, mediaName, pngBytes, cx, cy);
                            }
                        }
                        catch { }
                    }
                }
            }

            using (var ms = new MemoryStream(262144))
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteZipUtf8(zip, "[Content_Types].xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                        "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
                        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                        "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
                        "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>" +
                        "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>" +
                        "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                        "</Types>");

                    WriteZipUtf8(zip, "_rels/.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                        "</Relationships>");

                    WriteZipUtf8(zip, "docProps/core.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                        "<dc:title>Myanmar Spell Checker — Illustrated Bilingual A4 User Guide (Word, Excel &amp; PowerPoint)</dc:title>" +
                        "<dc:creator>Htet Yar Zar (ထက်ရာဇာ) — Department of Geology, Kyaukse University</dc:creator>" +
                        "<dc:description>Complete Offline Illustrated Bilingual User Guide, Annotated Screenshots &amp; Tool Reference</dc:description>" +
                        "</cp:coreProperties>");

                    var relsSb = new StringBuilder(2048);
                    relsSb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    relsSb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
                    relsSb.Append("<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                    relsSb.Append("<Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>");
                    relsSb.Append("<Relationship Id=\"rIdHeader1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>");
                    relsSb.Append("<Relationship Id=\"rIdFooter1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>");

                    foreach (var kv in embeddedImages)
                    {
                        relsSb.Append("<Relationship Id=\"").Append(kv.Value.RelId)
                              .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/")
                              .Append(kv.Value.MediaName).Append("\"/>");
                        WriteZipBinary(zip, "word/media/" + kv.Value.MediaName, kv.Value.Bytes);
                    }
                    relsSb.Append("</Relationships>");
                    WriteZipUtf8(zip, "word/_rels/document.xml.rels", relsSb.ToString());

                    WriteZipUtf8(zip, "word/styles.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                        "<w:docDefaults><w:rPrDefault><w:rPr>" +
                        "<w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\" w:cs=\"Pyidaungsu\" w:eastAsia=\"Pyidaungsu\"/>" +
                        "<w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/><w:noProof/>" +
                        "</w:rPr></w:rPrDefault></w:docDefaults>" +
                        "</w:styles>");

                    WriteZipUtf8(zip, "word/settings.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                        "<w:hideSpellingErrors/><w:hideGrammaticalErrors/>" +
                        "</w:settings>");

                    WriteZipUtf8(zip, "word/header1.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                        "<w:p><w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"4\" w:color=\"185ABC\"/></w:pBdr></w:pPr>" +
                        "<w:r><w:rPr><w:b/><w:color w:val=\"185ABC\"/><w:sz w:val=\"17\"/><w:noProof/></w:rPr>" +
                        "<w:t>Myanmar Spell Checker — Illustrated User Guide (မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး လမ်းညွှန် • Word | Excel | PowerPoint)</w:t></w:r></w:p>" +
                        "</w:hdr>");

                    WriteZipUtf8(zip, "word/footer1.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                        "<w:p><w:pPr><w:pBdr><w:top w:val=\"single\" w:sz=\"4\" w:space=\"4\" w:color=\"D0D8E8\"/></w:pBdr><w:jc w:val=\"both\"/></w:pPr>" +
                        "<w:r><w:rPr><w:color w:val=\"526075\"/><w:sz w:val=\"16\"/><w:noProof/></w:rPr>" +
                        "<w:t xml:space=\"preserve\">Htet Yar Zar (ထက်ရာဇာ), Dept. of Geology, Kyaukse University • GNU GPL v3.0   |   Page </w:t></w:r>" +
                        "<w:fldSimple w:instr=\"PAGE\"><w:r><w:rPr><w:sz w:val=\"16\"/><w:noProof/></w:rPr><w:t>1</w:t></w:r></w:fldSimple>" +
                        "<w:r><w:rPr><w:color w:val=\"526075\"/><w:sz w:val=\"16\"/><w:noProof/></w:rPr><w:t xml:space=\"preserve\"> of </w:t></w:r>" +
                        "<w:fldSimple w:instr=\"NUMPAGES\"><w:r><w:rPr><w:sz w:val=\"16\"/><w:noProof/></w:rPr><w:t>1</w:t></w:r></w:fldSimple>" +
                        "</w:p></w:ftr>");

                    WriteZipUtf8(zip, "word/document.xml", BuildWordDocumentXml(chapters, embeddedImages));
                }

                return ms.ToArray();
            }
        }

        private static string BuildWordDocumentXml(
            IReadOnlyList<GuideChapter> chapters,
            Dictionary<int, (string RelId, string MediaName, byte[] Bytes, long WidthEmu, long HeightEmu)> embeddedImages)
        {
            var sb = new StringBuilder(131072);
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" ");
            sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" ");
            sb.Append("xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" ");
            sb.Append("xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" ");
            sb.Append("xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">");
            sb.Append("<w:body>");

            // Cover Title Banner Table
            sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"").Append(ContentWidthTwips).Append("\" w:type=\"dxa\"/>");
            sb.Append("<w:tblBorders><w:top w:val=\"single\" w:sz=\"18\" w:space=\"0\" w:color=\"0F3D8A\"/>");
            sb.Append("<w:bottom w:val=\"single\" w:sz=\"12\" w:space=\"0\" w:color=\"185ABC\"/></w:tblBorders></w:tblPr>");
            sb.Append("<w:tr><w:tc><w:tcPr><w:tcW w:w=\"").Append(ContentWidthTwips).Append("\" w:type=\"dxa\"/>");
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"EEF4FF\"/>");
            sb.Append("<w:tcMar><w:top w:w=\"180\" w:type=\"dxa\"/><w:bottom w:w=\"180\" w:type=\"dxa\"/><w:left w:w=\"220\" w:type=\"dxa\"/><w:right w:w=\"220\" w:type=\"dxa\"/></w:tcMar></w:tcPr>");
            AppendDocxParagraph(sb, "Myanmar Spell Checker — Illustrated User Guide & Tool Handbook", "0F3D8A", 28, bold: true, spaceAfter: 60);
            AppendDocxParagraph(sb, "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး — ပုံနှင့်တကွ အသုံးပြုပုံလမ်းညွှန်အပြည့်အစုံ (Microsoft Word • Excel • PowerPoint)", "185ABC", 22, bold: true, spaceAfter: 80);
            AppendDocxParagraph(sb, "100% Offline 3-in-1 Office Suite • 183,038 Lexicon, Bilingual EN⇄MM & Encyclopedia Entries • Annotated Visual Edition", "334155", 18, bold: false, spaceAfter: 40);
            AppendDocxParagraph(sb, "Author: Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University • Dedication: U Moe Kyaw Thu (ဦးမိုးကျော်သူ) • Collaborators: Hein Htet Zaw & Thu Reain Khant", "475569", 17, bold: false, spaceAfter: 0);
            sb.Append("</w:tc></w:tr></w:tbl>");

            AppendDocxParagraph(sb, string.Empty, "1A2332", 12, bold: false, spaceAfter: 80);

            // Chapters
            for (int cIdx = 0; cIdx < chapters.Count; cIdx++)
            {
                var ch = chapters[cIdx];
                AppendDocxChapterHeading(sb, $"{ch.Number}. {ch.TitleEn} — {ch.TitleMm}");
                AppendDocxSummaryCallout(sb, ch.SummaryEn, ch.SummaryMm);

                foreach (var fig in ch.Figures)
                {
                    AppendDocxFigureWithCaptionCard(sb, fig, embeddedImages);
                    AppendDocxParagraph(sb, string.Empty, "1A2332", 8, bold: false, spaceAfter: 50);
                }

                foreach (var sub in ch.Subsections)
                {
                    AppendDocxParagraph(sb, $"{sub.HeadingEn}  |  {sub.HeadingMm}", "183B66", 21, bold: true, spaceAfter: 70, keepNext: true);

                    foreach (var (en, mm) in sub.Bullets)
                    {
                        sb.Append("<w:p><w:pPr><w:spacing w:before=\"30\" w:after=\"60\"/><w:ind w:left=\"280\" w:hanging=\"180\"/></w:pPr>");
                        AppendDocxRun(sb, "• ", "185ABC", 19, bold: true);
                        AppendDocxRun(sb, en + " — ", "1A2332", 19, bold: true);
                        AppendDocxRun(sb, mm, "334155", 19, bold: false);
                        sb.Append("</w:p>");
                    }

                    if (sub.TableHeaders != null && sub.TableHeaders.Length > 0 && sub.TableRows.Count > 0)
                    {
                        AppendDocxTable(sb, sub.TableHeaders, sub.TableRows);
                        AppendDocxParagraph(sb, string.Empty, "1A2332", 8, bold: false, spaceAfter: 40);
                    }
                }
            }

            // ISO A4 Portrait Section Properties
            sb.Append("<w:sectPr>");
            sb.Append("<w:headerReference w:type=\"default\" r:id=\"rIdHeader1\"/>");
            sb.Append("<w:footerReference w:type=\"default\" r:id=\"rIdFooter1\"/>");
            sb.Append("<w:pgSz w:w=\"").Append(IsoA4WidthTwips).Append("\" w:h=\"").Append(IsoA4HeightTwips).Append("\" w:orient=\"portrait\"/>");
            sb.Append("<w:pgMar w:top=\"").Append(MarginTopTwips).Append("\" w:right=\"").Append(MarginSideTwips)
              .Append("\" w:bottom=\"").Append(MarginBottomTwips).Append("\" w:left=\"").Append(MarginSideTwips)
              .Append("\" w:header=\"420\" w:footer=\"420\" w:gutter=\"0\"/>");
            sb.Append("</w:sectPr>");

            sb.Append("</w:body></w:document>");
            return sb.ToString();
        }

        private static void AppendDocxFigureWithCaptionCard(
            StringBuilder sb,
            GuideFigure fig,
            Dictionary<int, (string RelId, string MediaName, byte[] Bytes, long WidthEmu, long HeightEmu)> embeddedImages)
        {
            sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"").Append(ContentWidthTwips).Append("\" w:type=\"dxa\"/>");
            sb.Append("<w:tblBorders>");
            sb.Append("<w:top w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"93B4E8\"/>");
            sb.Append("<w:left w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"93B4E8\"/>");
            sb.Append("<w:bottom w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"93B4E8\"/>");
            sb.Append("<w:right w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"93B4E8\"/>");
            sb.Append("<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"D5E2F6\"/>");
            sb.Append("</w:tblBorders></w:tblPr>");

            if (embeddedImages.TryGetValue(fig.FigureIndex, out var img))
            {
                int docPrId = 100 + fig.FigureIndex;
                sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr><w:tc><w:tcPr><w:tcW w:w=\"").Append(ContentWidthTwips).Append("\" w:type=\"dxa\"/>");
                sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"EEF3FA\"/>");
                sb.Append("<w:tcMar><w:top w:w=\"100\" w:type=\"dxa\"/><w:bottom w:w=\"100\" w:type=\"dxa\"/><w:left w:w=\"100\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/></w:tcMar></w:tcPr>");
                sb.Append("<w:p><w:pPr><w:keepNext/><w:jc w:val=\"center\"/><w:spacing w:before=\"0\" w:after=\"0\"/></w:pPr>");
                sb.Append("<w:r><w:rPr><w:noProof/></w:rPr><w:drawing>");
                sb.Append("<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">");
                sb.Append("<wp:extent cx=\"").Append(img.WidthEmu).Append("\" cy=\"").Append(img.HeightEmu).Append("\"/>");
                sb.Append("<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>");
                sb.Append("<wp:docPr id=\"").Append(docPrId).Append("\" name=\"Figure ").Append(fig.FigureIndex).Append("\" descr=\"").Append(H(fig.TitleEn)).Append("\"/>");
                sb.Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>");
                sb.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">");
                sb.Append("<pic:pic><pic:nvPicPr><pic:cNvPr id=\"").Append(docPrId).Append("\" name=\"").Append(H(img.MediaName)).Append("\"/><pic:cNvPicPr/></pic:nvPicPr>");
                sb.Append("<pic:blipFill><a:blip r:embed=\"").Append(img.RelId).Append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>");
                sb.Append("<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(img.WidthEmu).Append("\" cy=\"").Append(img.HeightEmu).Append("\"/></a:xfrm>");
                sb.Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>");
                sb.Append("</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p></w:tc></w:tr>");
            }

            // Caption + Numbered Callout Explanation Cell
            sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr><w:tc><w:tcPr><w:tcW w:w=\"").Append(ContentWidthTwips).Append("\" w:type=\"dxa\"/>");
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F8FBFF\"/>");
            sb.Append("<w:tcMar><w:top w:w=\"100\" w:type=\"dxa\"/><w:bottom w:w=\"110\" w:type=\"dxa\"/><w:left w:w=\"150\" w:type=\"dxa\"/><w:right w:w=\"150\" w:type=\"dxa\"/></w:tcMar></w:tcPr>");

            AppendDocxParagraph(sb, $"Figure {fig.FigureIndex} (ပုံ {fig.FigureIndex}) — {fig.TitleEn} | {fig.TitleMm}", "0F3D8A", 19, bold: true, spaceAfter: 35);
            AppendDocxParagraph(sb, $"{fig.CaptionEn} ({fig.CaptionMm})", "334155", 17, bold: false, spaceAfter: 55);

            foreach (var co in fig.Callouts)
            {
                string badge = FormatCircledNumber(co.Number);
                sb.Append("<w:p><w:pPr><w:spacing w:before=\"15\" w:after=\"30\"/><w:ind w:left=\"220\" w:hanging=\"220\"/></w:pPr>");
                AppendDocxRun(sb, badge + " ", "C92A2A", 18, bold: true);
                AppendDocxRun(sb, co.LabelEn + " (" + co.LabelMm + "): ", "0F3D8A", 17, bold: true);
                AppendDocxRun(sb, co.ExplainEn + " — " + co.ExplainMm, "1E293B", 17, bold: false);
                sb.Append("</w:p>");
            }

            sb.Append("</w:tc></w:tr></w:tbl>");
        }

        private static bool TryReadPngDimensions(byte[] pngBytes, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (pngBytes == null || pngBytes.Length < 24)
                return false;
            if (pngBytes[0] != 0x89 || pngBytes[1] != 0x50 || pngBytes[2] != 0x4E || pngBytes[3] != 0x47)
                return false;

            width = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
            height = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];
            return width > 0 && height > 0;
        }

        private static string FormatCircledNumber(int n)
        {
            return n switch
            {
                1 => "①",
                2 => "②",
                3 => "③",
                4 => "④",
                5 => "⑤",
                6 => "⑥",
                7 => "⑦",
                8 => "⑧",
                9 => "⑨",
                _ => $"({n})"
            };
        }

        private static void AppendDocxChapterHeading(StringBuilder sb, string text)
        {
            sb.Append("<w:p><w:pPr><w:keepNext/><w:spacing w:before=\"180\" w:after=\"70\"/>");
            sb.Append("<w:pBdr><w:bottom w:val=\"single\" w:sz=\"8\" w:space=\"3\" w:color=\"185ABC\"/></w:pBdr></w:pPr>");
            AppendDocxRun(sb, text, "0F3D8A", 24, bold: true);
            sb.Append("</w:p>");
        }

        private static void AppendDocxSummaryCallout(StringBuilder sb, string en, string mm)
        {
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"20\" w:after=\"90\"/><w:ind w:left=\"140\" w:right=\"100\"/>");
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F4F8FF\"/></w:pPr>");
            AppendDocxRun(sb, en + "  ", "1E293B", 18, bold: true);
            AppendDocxRun(sb, "(" + mm + ")", "334155", 18, bold: false);
            sb.Append("</w:p>");
        }

        private static void AppendDocxParagraph(StringBuilder sb, string text, string hexColor, int halfPtSize, bool bold, int spaceAfter, bool keepNext = false)
        {
            sb.Append("<w:p><w:pPr>");
            if (keepNext) sb.Append("<w:keepNext/>");
            sb.Append("<w:spacing w:before=\"20\" w:after=\"").Append(spaceAfter).Append("\"/></w:pPr>");
            if (!string.IsNullOrEmpty(text))
            {
                AppendDocxRun(sb, text, hexColor, halfPtSize, bold);
            }
            sb.Append("</w:p>");
        }

        private static void AppendDocxRun(StringBuilder sb, string text, string hexColor, int halfPtSize, bool bold)
        {
            sb.Append("<w:r><w:rPr><w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\" w:cs=\"Pyidaungsu\" w:eastAsia=\"Pyidaungsu\"/>");
            if (bold) sb.Append("<w:b/><w:bCs/>");
            sb.Append("<w:color w:val=\"").Append(hexColor).Append("\"/>");
            sb.Append("<w:sz w:val=\"").Append(halfPtSize).Append("\"/><w:szCs w:val=\"").Append(halfPtSize).Append("\"/>");
            sb.Append("<w:noProof/></w:rPr><w:t xml:space=\"preserve\">").Append(H(text)).Append("</w:t></w:r>");
        }

        private static void AppendDocxTable(StringBuilder sb, string[] headers, List<string[]> rows)
        {
            int colCount = headers.Length;
            int[] widths = ComputeColumnWidths(colCount);

            sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"").Append(ContentWidthTwips).Append("\" w:type=\"dxa\"/>");
            sb.Append("<w:tblBorders>");
            sb.Append("<w:top w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:left w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:right w:val=\"single\" w:sz=\"6\" w:space=\"0\" w:color=\"CBD5E1\"/>");
            sb.Append("<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"E2E8F0\"/>");
            sb.Append("<w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"E2E8F0\"/>");
            sb.Append("</w:tblBorders></w:tblPr>");

            // Header Row (repeating on every A4 page via <w:tblHeader/>)
            sb.Append("<w:tr><w:trPr><w:tblHeader/><w:cantSplit/></w:trPr>");
            for (int c = 0; c < colCount; c++)
            {
                sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(widths[c]).Append("\" w:type=\"dxa\"/>");
                sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"185ABC\"/>");
                sb.Append("<w:tcMar><w:top w:w=\"80\" w:type=\"dxa\"/><w:bottom w:w=\"80\" w:type=\"dxa\"/><w:left w:w=\"100\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/></w:tcMar></w:tcPr>");
                sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/></w:pPr>");
                AppendDocxRun(sb, headers[c], "FFFFFF", 18, bold: true);
                sb.Append("</w:p></w:tc>");
            }
            sb.Append("</w:tr>");

            for (int r = 0; r < rows.Count; r++)
            {
                string[] row = rows[r];
                string fill = (r % 2 == 1) ? "F8FAFC" : "FFFFFF";
                sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
                for (int c = 0; c < colCount; c++)
                {
                    string val = c < row.Length ? (row[c] ?? string.Empty) : string.Empty;
                    sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(widths[c]).Append("\" w:type=\"dxa\"/>");
                    sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(fill).Append("\"/>");
                    sb.Append("<w:tcMar><w:top w:w=\"70\" w:type=\"dxa\"/><w:bottom w:w=\"70\" w:type=\"dxa\"/><w:left w:w=\"100\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/></w:tcMar></w:tcPr>");
                    sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/></w:pPr>");
                    AppendDocxRun(sb, val, "1E293B", 18, bold: c == 0);
                    sb.Append("</w:p></w:tc>");
                }
                sb.Append("</w:tr>");
            }

            sb.Append("</w:tbl>");
        }

        private static int[] ComputeColumnWidths(int colCount)
        {
            if (colCount == 4)
            {
                return new[] { 1800, 2400, 1600, ContentWidthTwips - 5800 };
            }
            if (colCount == 3)
            {
                return new[] { 2300, 2600, ContentWidthTwips - 4900 };
            }
            int each = ContentWidthTwips / Math.Max(1, colCount);
            var arr = new int[colCount];
            for (int i = 0; i < colCount; i++)
                arr[i] = (i == colCount - 1) ? (ContentWidthTwips - each * (colCount - 1)) : each;
            return arr;
        }

        private static void WriteZipUtf8(ZipArchive zip, string entryPath, string xmlContent)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(xmlContent);
            }
        }

        private static void WriteZipBinary(ZipArchive zip, string entryPath, byte[] bytes)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static string H(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var sb = new StringBuilder(raw!.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\t' || c == '\n' || c == '\r' ||
                    (c >= 0x20 && !(c >= 0xD800 && c <= 0xDFFF) && c != 0xFFFE && c != 0xFFFF))
                {
                    sb.Append(c);
                }
            }
            return SecurityElement.Escape(sb.ToString()) ?? string.Empty;
        }
    }
}
