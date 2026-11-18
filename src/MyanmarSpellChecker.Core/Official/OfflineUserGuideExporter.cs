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
    /// Single-source-of-truth Bilingual (English + မြန်မာဘာသာ) User Guide generator and launcher for the
    /// Myanmar Spell Checker 3-in-1 Microsoft Office Suite (Word + Excel + PowerPoint).
    /// Produces three 100% offline formats with zero external NuGet dependencies:
    /// 1. GitHub Flavored Markdown (<c>docs/USER_GUIDE.md</c>)
    /// 2. Interactive Searchable Offline HTML Handbook (<c>Myanmar-Spell-Checker-User-Guide.html</c>) with live
    ///    search filter, [Bilingual | English | မြန်မာ] language toggle, sticky TOC, and Print/PDF stylesheet
    /// 3. Print-Ready ISO A4 Portrait Microsoft Word Manual (<c>Myanmar-Spell-Checker-User-Guide.docx</c>) with
    ///    repeating table headers, running A4 header/footer, &lt;w:noProof/&gt;, and Pyidaungsu + Segoe UI typography.
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
        private const int ContentWidthTwips = IsoA4WidthTwips - (MarginSideTwips * 2); // 10,318 twips

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

        public static string EnsureInteractiveHtmlGuideOnDisk(string? directoryOverride = null)
        {
            string dir = !string.IsNullOrWhiteSpace(directoryOverride)
                ? directoryOverride!
                : GetDefaultUserGuideDirectory();
            Directory.CreateDirectory(dir);
            string fullPath = Path.Combine(dir, HtmlGuideFileName);
            File.WriteAllText(fullPath, BuildInteractiveHtmlUserGuide(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return fullPath;
        }

        public static string EnsurePrintableWordGuideOnDisk(string? directoryOverride = null)
        {
            string dir = !string.IsNullOrWhiteSpace(directoryOverride)
                ? directoryOverride!
                : GetDefaultUserGuideDirectory();
            Directory.CreateDirectory(dir);
            string fullPath = Path.Combine(dir, DocxGuideFileName);
            File.WriteAllBytes(fullPath, BuildPrintableA4WordUserGuideBytes());
            return fullPath;
        }

        public static string EnsureMarkdownGuideOnDisk(string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
            string fullPath = Path.Combine(directoryPath, MarkdownGuideFileName);
            File.WriteAllText(fullPath, BuildMarkdownUserGuide(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return fullPath;
        }

        public static string OpenInteractiveHtmlGuide(string? preferredSearchDir = null)
        {
            // Also materialize sibling Printable Word .docx in the same folder so the link inside HTML works offline!
            string dir = GetDefaultUserGuideDirectory();
            try { EnsurePrintableWordGuideOnDisk(dir); } catch { }
            string htmlPath = EnsureInteractiveHtmlGuideOnDisk(dir);
            _ = preferredSearchDir;

            Process.Start(new ProcessStartInfo(htmlPath)
            {
                UseShellExecute = true
            });
            return htmlPath;
        }

        public static string OpenPrintableWordGuide(string? preferredSearchDir = null)
        {
            _ = preferredSearchDir;
            string docxPath = EnsurePrintableWordGuideOnDisk(GetDefaultUserGuideDirectory());
            Process.Start(new ProcessStartInfo(docxPath)
            {
                UseShellExecute = true
            });
            return docxPath;
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
                new[] { "Dictionary (အဘိဓာန်)", "181,497-Entry Bilingual EN ⇄ MM Search", "Word, Excel, PPT", "Instant offline lookup by Myanmar, English, Romanized Burmese ('mingalaba', 'kyaukse'), or Zawgyi. (မြန်မာ၊ အင်္ဂလိပ်၊ ရိုမန်အသံထွက် တို့ဖြင့် ရှာနိုင်သည်။)" },
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
            var ch3s1 = new GuideSubsection
            {
                HeadingEn = "3.1 Color-Coded Wavy Underline Legend (Red / Blue / Amber)",
                HeadingMm = "၃.၁ မျဉ်းလှိုင်းအရောင် (၃) မျိုး၏ အဓိပ္ပာယ်နှင့် Auto-Fix လုံခြုံရေးစနစ်",
                TableHeaders = new[] { "Underline Color", "Category (အမျိုးအစား)", "Real Examples (ဥပမာများ)", "Batch Fix Behavior" }
            };
            ch3s1.TableRows.AddRange(new[]
            {
                new[] { "Red Wavy (အနီရောင်)", "Spelling & High-Certainty Homophone (သတ်ပုံအမှား)", "ပီးပီ ➔ ပြီးပြီ, ဥက္ကဌ ➔ ဥက္ကဋ္ဌ, သကေင်္တ ➔ သင်္ကေတ, စာတိုက်ကုတ် ➔ ကုဒ်", "✓ Auto-Checked (Safe for 1-click Fix All)" },
                new[] { "Blue Wavy (အပြာရောင်)", "Grammar, Classifier & Register Style (သဒ္ဒါနှင့် စာဟန်)", "တခု ➔ တစ်ခု, မသိပဲ ➔ မသိဘဲ, ဖိနပ် တစ်ခု ➔ တစ်ရံ, ပါတယ် ➔ ပါသည်", "✓ Auto-Checked (Safe for 1-click Fix All)" },
                new[] { "Amber Wavy (လိမ္မော်ရောင်)", "Contextual Meaning, Intruder Word & SOV Order (အနက်သံသယ / စိစစ်ရန်)", "ဥပဒေ ချိုးဖဲ့ ➔ ချိုးဖောက်, နည်းပညာ စား အသစ် ➔ အသစ်, တင်သွင်းခဲ့သည် အစီရင်ခံစာကို ➔ SOV", "☐ Unchecked by Default ('Review Needed' — human confirms first!)" }
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
                new[] { "General Standard (အထွေထွေစံ)", "Articles, emails, business campaigns, speeches, textbooks", "Polite spoken particles (ပါတယ်, မယ်, တွေ, နဲ့, ဘူး)", "All 462-page သတ်ပုံကျမ်း spelling, တ/တစ် rules, پဲ/ဘဲ, လဲ/လည်း, classifiers & majority-register clashes" },
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
                "Mixed Myanmar + English Smart Shield: Automatically marks Burmese script spans with NoProofing=1 so Word's US-English spellchecker never underlines valid Burmese words in red, while keeping English words (e.g., 'Tiktok', 'Licencing') checked in English and preserving your intentional single/double heading underlines!",
                "မြန်မာနှင့် အင်္ဂလိပ် ရောရေးထားသော စာပိုဒ်များတွင် မြန်မာစာသားများကို Word ၏ English Spellcheck အနီမျဉ်း မတက်စေရန် အလိုအလျောက် ကာကွယ်ပေးပြီး အင်္ဂလိပ်စာလုံးပေါင်းစစ်ဆေးမှုနှင့် ခေါင်းစဉ်မျဉ်းသားထားမှု (Single Underline) များကို မူလအတိုင်း ထိန်းသိမ်းပေးပါသည်။"
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
                SummaryEn = "Search 96,263 official Burmese headwords, 57,064 English-Myanmar entries, 25,001 Encyclopedia articles, 1,929 Proverbs, 9,221 Gazetteer places, and 8 Specialized Domains with zero internet.",
                SummaryMm = "အင်တာနက်လုံးဝမလိုဘဲ သတ်ပုံကျမ်း (၉၆,၂၆၃ ပုဒ်)၊ အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန် (၅၇,၀၆၄ ပုဒ်)၊ စွယ်စုံဗဟုသုတ (၂၅,၀၀၁ ပုဒ်)၊ စကားပုံ (၁,၉၂၉ ပုဒ်) နှင့် ပညာရပ်နယ်ပယ် (၈) ခုကို ရှာဖွေနိုင်ပါသည်။"
            };
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
                new[] { "Stage 1", "Canonical Unicode & O(N) Syllable FSM", "Normalizes diacritic order (U+1031/U+103A/U+1037), detects Zawgyi/WinInnwa/garbled PDF encoding, fixes lookalikes (၀ ➔ ဝ, ၄င်း ➔ ၎င်း), and segments syllables in ~0.02 ms without regex backtracking." },
                new[] { "Stage 2", "AES-256 + HMAC-SHA256 Binary Trie", "Loads 96,263 active headwords and 109,880 open-addressed O(1) word bigrams into a compact Trie while keeping 83,994 bilingual/encyclopedia definitions lazily hydrated on disk." },
                new[] { "Stage 3", "K=3 Beam Viterbi + Bigram LM + 45-Bit Pruner", "Segments unspaced Burmese text using a Hypothesis-Recombining Beam Viterbi lattice scored by an interpolated Witten-Bell Bigram/Unigram Language Model, pruning typo candidates via 45-bit consonant masks and phonetic confusion matrices." },
                new[] { "Stage 4", "Two-Tier Confusion Sets, Intruder & SOV Parser", "Catches real-word homophones (လျော်/လျှော်, ပစ်မှု/ပြစ်မှု, ကုတ်/ကုဒ်), statistical 1-syllable intruder typos inside compounds, temporal/aspect clashes, Majority-Register Concord, and leaked post-verbal SOV arguments." },
                new[] { "Stage 5", "Host-Safe Non-Destructive Rendering", "Uses live Word.Range bindings + surgical Burmese-span NoProofing=1 in Word, and per-pixel alpha click-through Win32 layered overlays in Excel & PowerPoint so .xlsx/.pptx files are never mutated." },
                new[] { "Stage 6", "EcoPerformanceGovernor Time-Slicing", "Adapts chunk sizes and caches to CPU/RAM hardware (< 35 MB core proofing heap, p50 ~ 2.1 ms per sentence) so Office stays responsive even on 4 GB RAM laptops." }
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
                SummaryEn = "Quick answers for installing on another PC, Protected View files, COM Add-ins settings, and open-source GPLv3 attribution.",
                SummaryMm = "အခြားကွန်ပျူတာတွင် သွင်းယူရာ၌ ကြုံတတ်သော အချက်များ ဖြေရှင်းနည်းနှင့် ဖန်တီးသူ မှတ်တမ်း။"
            };
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
            var sb = new StringBuilder(32768);

            sb.AppendLine("# Myanmar Spell Checker — Complete User Guide & Architecture Handbook");
            sb.AppendLine("## မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး — အသုံးပြုပုံလမ်းညွှန်အပြည့်အစုံ (Word • Excel • PowerPoint)");
            sb.AppendLine();
            sb.AppendLine("> **100% Offline Unified 3-in-1 Microsoft Office Suite** (`Word + Excel + PowerPoint`, `.NET Framework 4.8`)  ");
            sb.AppendLine("> **Author:** **Htet Yar Zar (ထက်ရာဇာ)** — Department of Geology, Kyaukse University, Kyaukse, Myanmar  ");
            sb.AppendLine("> **Special Dedication:** **U Moe Kyaw Thu (ဦးမိုးကျော်သူ)** • **Collaborators:** **Hein Htet Zaw (ဟိန်းထက်ဇော်)** & **Thu Reain Khant (သူရိန်ခန့်)**  ");
            sb.AppendLine("> **License:** GNU GPL v3.0 + Mandatory Section 7(b) Attribution (`181,497` Compiled Entries • Zero Internet Calls)");
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

        public static string BuildInteractiveHtmlUserGuide()
        {
            var chapters = BuildStructuredChapters();
            var sb = new StringBuilder(65536);

            sb.Append(@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>Myanmar Spell Checker — Complete User Guide (မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး လမ်းညွှန်)</title>
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
  .topbar, .sidebar { display:none !important; }
  .layout { display:block; padding:0; max-width:none; }
  .chapter, .hero { break-inside:avoid; box-shadow:none; border:1px solid #ccc; }
  body { background:#fff; font-size:10pt; }
}
</style>
</head>
<body data-lang=""both"">
<header class=""topbar"">
  <div>
    <div class=""topbar-title"">Myanmar Spell Checker — User Guide &amp; Tool Handbook</div>
    <div class=""topbar-sub"">မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး အသုံးပြုပုံလမ်းညွှန် (Word • Excel • PowerPoint — 100% Offline)</div>
  </div>
  <div class=""toolbar"">
    <input id=""searchBox"" class=""search-input"" type=""search"" placeholder=""Search tools, rules, FAQ... (ရှာရန်)"" oninput=""filterGuide(this.value)"" />
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
        <div class=""kpi""><strong>181,497 Entries</strong><span>96,263 Headwords + 57,064 EN⇄MM + 25,001 Encyclopedia</span></div>
        <div class=""kpi""><strong>Word • Excel • PPT</strong><span>Unified Ribbon + Review Tab + 4-Tab Task Pane</span></div>
        <div class=""kpi""><strong>3 Writing Modes</strong><span>Casual / Chat • General Standard • Official / Literary</span></div>
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
</script>
</body>
</html>");

            return sb.ToString();
        }

        // ==================== 3. PRINTABLE A4 WORD (.DOCX) GENERATOR ====================

        public static byte[] BuildPrintableA4WordUserGuideBytes()
        {
            var chapters = BuildStructuredChapters();

            using (var ms = new MemoryStream(131072))
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteZipUtf8(zip, "[Content_Types].xml",
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
                        "</Types>");

                    WriteZipUtf8(zip, "_rels/.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                        "</Relationships>");

                    WriteZipUtf8(zip, "docProps/core.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                        "<dc:title>Myanmar Spell Checker — Bilingual A4 User Guide (Word, Excel &amp; PowerPoint)</dc:title>" +
                        "<dc:creator>Htet Yar Zar (ထက်ရာဇာ) — Department of Geology, Kyaukse University</dc:creator>" +
                        "<dc:description>Complete Offline Bilingual User Guide, Tool Directory &amp; Architecture Reference</dc:description>" +
                        "</cp:coreProperties>");

                    WriteZipUtf8(zip, "word/_rels/document.xml.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                        "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                        "<Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
                        "<Relationship Id=\"rIdHeader1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>" +
                        "<Relationship Id=\"rIdFooter1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>" +
                        "</Relationships>");

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
                        "<w:t>Myanmar Spell Checker — Bilingual User Guide (မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး လမ်းညွှန် • Word | Excel | PowerPoint)</w:t></w:r></w:p>" +
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

                    WriteZipUtf8(zip, "word/document.xml", BuildWordDocumentXml(chapters));
                }

                return ms.ToArray();
            }
        }

        private static string BuildWordDocumentXml(IReadOnlyList<GuideChapter> chapters)
        {
            var sb = new StringBuilder(98304);
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" ");
            sb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<w:body>");

            // Cover Title Banner Table
            sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"").Append(ContentWidthTwips).Append("\" w:type=\"dxa\"/>");
            sb.Append("<w:tblBorders><w:top w:val=\"single\" w:sz=\"18\" w:space=\"0\" w:color=\"0F3D8A\"/>");
            sb.Append("<w:bottom w:val=\"single\" w:sz=\"12\" w:space=\"0\" w:color=\"185ABC\"/></w:tblBorders></w:tblPr>");
            sb.Append("<w:tr><w:tc><w:tcPr><w:tcW w:w=\"").Append(ContentWidthTwips).Append("\" w:type=\"dxa\"/>");
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"EEF4FF\"/>");
            sb.Append("<w:tcMar><w:top w:w=\"180\" w:type=\"dxa\"/><w:bottom w:w=\"180\" w:type=\"dxa\"/><w:left w:w=\"220\" w:type=\"dxa\"/><w:right w:w=\"220\" w:type=\"dxa\"/></w:tcMar></w:tcPr>");
            AppendDocxParagraph(sb, "Myanmar Spell Checker — Complete User Guide & Tool Handbook", "0F3D8A", 28, bold: true, spaceAfter: 60);
            AppendDocxParagraph(sb, "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး — အသုံးပြုပုံလမ်းညွှန်အပြည့်အစုံ (Microsoft Word • Excel • PowerPoint)", "185ABC", 22, bold: true, spaceAfter: 80);
            AppendDocxParagraph(sb, "100% Offline 3-in-1 Office Suite • 181,497 Lexicon, Bilingual EN⇄MM & Encyclopedia Entries • Printable ISO A4 Edition", "334155", 18, bold: false, spaceAfter: 40);
            AppendDocxParagraph(sb, "Author: Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University • Dedication: U Moe Kyaw Thu (ဦးမိုးကျော်သူ) • Collaborators: Hein Htet Zaw & Thu Reain Khant", "475569", 17, bold: false, spaceAfter: 0);
            sb.Append("</w:tc></w:tr></w:tbl>");

            AppendDocxParagraph(sb, string.Empty, "1A2332", 12, bold: false, spaceAfter: 80);

            // Chapters
            for (int cIdx = 0; cIdx < chapters.Count; cIdx++)
            {
                var ch = chapters[cIdx];
                AppendDocxChapterHeading(sb, $"{ch.Number}. {ch.TitleEn} — {ch.TitleMm}");
                AppendDocxSummaryCallout(sb, ch.SummaryEn, ch.SummaryMm);

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
