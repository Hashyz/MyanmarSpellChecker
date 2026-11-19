// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Performance;
using MyanmarSpellChecker.Core.Presentation;
using MyanmarSpellChecker.Core.Security;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.ExcelAddIn.UI;
using MyanmarSpellChecker.PowerPointAddIn.UI;
using MyanmarSpellChecker.WordAddIn.Interop;
using MyanmarSpellChecker.WordAddIn.UI;

namespace MyanmarSpellChecker.AdminTools.Guide
{
    /// <summary>
    /// Renders 8 high-resolution, real-UI annotated PNG screenshots (with numbered callout badges 1..5)
    /// from the live WinForms Task Panes, Quick-Fix Popup, Modeless Review &amp; Fix Studio, and About Dialog
    /// backed by the real <see cref="MyanmarProofingEngine"/> and <c>mlc_thatpon.db</c>.
    /// </summary>
    internal static class GuideScreenshotGenerator
    {
        private const int CanvasW = 1240;
        private const int CanvasH = 740;
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        public static int RenderAllGuideScreenshots(string outputImagesDir)
        {
            Directory.CreateDirectory(outputImagesDir);
            int count = 0;
            Exception? capturedError = null;

            var staThread = new Thread(() =>
            {
                UiLanguage savedLang = UiLocalizer.CurrentLanguage;
                try
                {
                    Application.EnableVisualStyles();
                    UiLocalizer.CurrentLanguage = UiLanguage.English;

                    string dbPath = ResolveDatabasePath();
                    var repo = new EncryptedDictionaryRepository(dbPath);
                    MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
                    var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
                    var standardEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
                    formalEngine.PrewarmDictionaryIndexes();

                    RenderFig01RibbonAndWorkspaceOverview(outputImagesDir, formalEngine, trie);
                    count++;

                    RenderFig02WordLiveProofingAndQuickFix(outputImagesDir, formalEngine, trie);
                    count++;

                    RenderFig03ReviewAndFixStudio(outputImagesDir, formalEngine);
                    count++;

                    RenderFig04ExcelFormulaSafeProofing(outputImagesDir, formalEngine, trie);
                    count++;

                    RenderFig05PowerPointSlideProofing(outputImagesDir, formalEngine, trie);
                    count++;

                    RenderFig06BilingualDictionaryAndEncyclopedia(outputImagesDir, formalEngine, trie);
                    count++;

                    RenderFig07OfficialWritingToolkit(outputImagesDir, formalEngine, trie);
                    count++;

                    RenderFig08BilingualMyanmarUiAndAbout(outputImagesDir, standardEngine, trie);
                    count++;
                }
                catch (Exception ex)
                {
                    capturedError = ex;
                }
                finally
                {
                    UiLocalizer.CurrentLanguage = savedLang;
                }
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            if (capturedError != null)
                throw new InvalidOperationException("Failed to render User Guide screenshots: " + capturedError.Message, capturedError);

            return count;
        }

        private static string ResolveDatabasePath()
        {
            string[] candidates =
            {
                Path.Combine("data", "mlc_thatpon.db"),
                Path.Combine("artifacts", "mlc_thatpon.db"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mlc_thatpon.db")
            };

            foreach (string c in candidates)
            {
                if (File.Exists(c) && new FileInfo(c).Length > 5_000_000)
                    return Path.GetFullPath(c);
            }

            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string probe = Path.Combine(dir.FullName, "data", "mlc_thatpon.db");
                if (File.Exists(probe))
                    return probe;
            }

            throw new FileNotFoundException("Could not locate data/mlc_thatpon.db for screenshot generation.");
        }

        private static Font CreateMyanmarUiFont(float sizePt, FontStyle style = FontStyle.Regular)
        {
            foreach (string name in new[] { "Pyidaungsu", "Myanmar Text", "Segoe UI" })
            {
                try
                {
                    var f = new Font(name, sizePt, style, GraphicsUnit.Point);
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                        return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(FontFamily.GenericSansSerif, sizePt, style);
        }

        private static Bitmap CaptureWinFormsControl(Control ctrl, int width, int height)
        {
            using (var host = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-3200, -3200),
                Size = new Size(width, height),
                ShowInTaskbar = false,
                BackColor = ctrl.BackColor
            })
            {
                ctrl.Dock = DockStyle.Fill;
                host.Controls.Add(ctrl);
                host.CreateControl();
                host.Show();
                host.PerformLayout();
                ctrl.PerformLayout();
                host.Refresh();
                Application.DoEvents();
                Thread.Sleep(35);
                Application.DoEvents();

                var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                bool capturedViaPrintWindow = false;
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        capturedViaPrintWindow = PrintWindow(host.Handle, hdc, PW_RENDERFULLCONTENT);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                if (!capturedViaPrintWindow || IsMostlyBlank(bmp))
                {
                    ctrl.DrawToBitmap(bmp, new Rectangle(0, 0, width, height));
                }

                host.Controls.Remove(ctrl);
                host.Hide();
                return bmp;
            }
        }

        private static Bitmap CaptureStandaloneForm(Form form, int width, int height)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-3200, -3200);
            form.Size = new Size(width, height);
            form.ShowInTaskbar = false;
            form.Show();
            form.PerformLayout();
            form.Refresh();
            Application.DoEvents();
            Thread.Sleep(45);
            Application.DoEvents();

            var bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
            bool ok = false;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    ok = PrintWindow(form.Handle, hdc, PW_RENDERFULLCONTENT);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            if (!ok || IsMostlyBlank(bmp))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            }

            form.Hide();
            return bmp;
        }

        private static bool IsMostlyBlank(Bitmap bmp)
        {
            Color first = bmp.GetPixel(Math.Min(12, bmp.Width - 1), Math.Min(12, bmp.Height - 1));
            int diff = 0;
            for (int y = 16; y < bmp.Height - 16; y += 28)
            {
                for (int x = 16; x < bmp.Width - 16; x += 36)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R != first.R || c.G != first.G || c.B != first.B)
                    {
                        diff++;
                        if (diff >= 8)
                            return false;
                    }
                }
            }
            return true;
        }

        private static void WireWordTaskPane(ProofingTaskPaneControl pane, MyanmarProofingEngine engine, MyanmarTrie trie, string sampleDocText)
        {
            pane.SetLexiconStats(trie.WordCount, trie.OfficialThatponCount, trie.RuleCount, trie.ReferenceCount, trie.EncyclopediaCount);
            pane.SetStyleMode(engine.StyleMode);
            pane.SetEcoModeState(EcoModePreference.Auto, false);
            pane.RequestIsEngineReady += () => true;
            pane.RequestIsDictionaryIndexReady += () => true;
            pane.RequestDictionarySearch += q => engine.SearchDictionary(q, 18);
            pane.RequestSearchReferenceCorpus += (q, b, e) => engine.SearchReferenceCorpus(q, b, e, 18);
            pane.RequestHomophoneSummary += w => engine.GetHomophoneSiblingsSummary(w, 3);
            pane.RequestGetSynonyms += w => engine.GetSynonyms(w, 6);
            pane.RequestGetAntonyms += w => engine.GetAntonyms(w, 6);
            pane.RequestGetCollocations += w => engine.GetTopCollocations(w, 6);
            pane.RequestGetFeaturedKnowledge += () => engine.GetFeaturedKnowledgeEntries(6);
            pane.RequestGetFeaturedProverbs += () => engine.GetFeaturedProverbEntries(6);
            pane.RequestGetSelectedDocumentText += () => "1,500,000";
            pane.RequestGetCustomWords += () => new[]
            {
                "ကျောက်ဆည်တက္ကသိုလ်",
                "ဘူမိဗေဒဌာန",
                "ထက်ရာဇာ",
                "ဟူးကောင်းခန်းမ",
                "MSME-2026"
            };
            pane.RequestRefreshQualityReport += () =>
            {
                var issues = engine.CheckText(sampleDocText, cacheParagraph: false);
                return DocumentQualityAnalyzer.Analyze(sampleDocText, issues.ToList());
            };
        }

        private static List<DocumentProofingItem> BuildSampleWordDocumentItems(MyanmarProofingEngine engine, out string sampleParagraph)
        {
            sampleParagraph =
                "ပြည်ထောင်စုအစိုးရအဖွဲ့ရုံးက အမိန့်ကြေညာစာ တခုကို ထုတ်ပြန်ပြီး ကောင်စီဥက္ကဌက ရေခဲသတ္တာနှင့် လုပ်ငန်းမျာအား စီစစ်ပါတယ်။";
            var rawIssues = engine.CheckText(sampleParagraph, cacheParagraph: false);
            var list = new List<DocumentProofingItem>();
            int idx = 1;
            foreach (var iss in rawIssues)
            {
                list.Add(new DocumentProofingItem
                {
                    StoryName = "Body",
                    ParagraphIndex = idx <= 3 ? 1 : 2,
                    PageNumber = 1,
                    LineInParagraph = idx,
                    HasResolvedFriendlyLocation = true,
                    ParagraphSnippet = sampleParagraph,
                    CachedStart = iss.StartIndex,
                    CachedEnd = iss.StartIndex + iss.Length,
                    Issue = iss
                });
                idx++;
            }
            return list;
        }

        private static void ConfigureHighQualityGraphics(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        private static void DrawOfficeWindowChrome(
            Graphics g,
            Color appBrandColor,
            string windowTitle,
            string activeRibbonTab,
            (string Group, string[] Buttons)[] ribbonGroups,
            int ribbonHeight = 114)
        {
            // Top Title Bar (34px)
            using (var titleBrush = new SolidBrush(appBrandColor))
                g.FillRectangle(titleBrush, 0, 0, CanvasW, 34);

            using (var fTitle = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold))
            using (var white = new SolidBrush(Color.White))
            {
                g.DrawString(windowTitle, fTitle, white, 16, 8);
                g.DrawString("—   □   ✕", fTitle, white, CanvasW - 88, 8);
            }

            // Ribbon Tab Strip (28px: y=34..62)
            using (var tabStripBrush = new SolidBrush(appBrandColor))
                g.FillRectangle(tabStripBrush, 0, 34, CanvasW, 28);

            string[] tabs = { "File", "Home", "Insert", "Layout", "Review", activeRibbonTab, "View" };
            int tx = 14;
            using (var fTab = new Font("Segoe UI", 9.0F, FontStyle.Regular))
            using (var fTabActive = new Font("Segoe UI Semibold", 9.0F, FontStyle.Bold))
            {
                foreach (string t in tabs)
                {
                    bool isActive = string.Equals(t, activeRibbonTab, StringComparison.Ordinal);
                    int tw = (int)Math.Ceiling(g.MeasureString(t, isActive ? fTabActive : fTab).Width) + 22;
                    if (isActive)
                    {
                        using (var cardB = new SolidBrush(Color.FromArgb(246, 248, 252)))
                            g.FillRectangle(cardB, tx, 36, tw, 26);
                        using (var brandTxt = new SolidBrush(appBrandColor))
                            g.DrawString(t, fTabActive, brandTxt, tx + 10, 40);
                    }
                    else
                    {
                        using (var wTxt = new SolidBrush(Color.FromArgb(238, 244, 255)))
                            g.DrawString(t, fTab, wTxt, tx + 10, 40);
                    }
                    tx += tw + 4;
                }
            }

            // Ribbon Command Surface (y=62..62+ribbonHeight-62)
            int cmdTop = 62;
            int cmdH = ribbonHeight - cmdTop;
            using (var cmdBg = new SolidBrush(Color.FromArgb(246, 248, 252)))
                g.FillRectangle(cmdBg, 0, cmdTop, CanvasW, cmdH);
            using (var borderPen = new Pen(Color.FromArgb(212, 220, 232), 1F))
                g.DrawLine(borderPen, 0, ribbonHeight - 1, CanvasW, ribbonHeight - 1);

            int gx = 12;
            using (var fBtn = CreateMyanmarUiFont(8.6F, FontStyle.Bold))
            using (var fGrp = new Font("Segoe UI", 7.4F, FontStyle.Regular))
            using (var btnFill = new SolidBrush(Color.White))
            using (var btnBorder = new Pen(Color.FromArgb(196, 208, 226), 1F))
            using (var btnText = new SolidBrush(Color.FromArgb(24, 42, 68)))
            using (var grpText = new SolidBrush(Color.FromArgb(102, 114, 132)))
            using (var sepPen = new Pen(Color.FromArgb(218, 225, 235), 1F))
            {
                foreach (var grp in ribbonGroups)
                {
                    int startGx = gx;
                    foreach (string bLabel in grp.Buttons)
                    {
                        int bw = (int)Math.Ceiling(g.MeasureString(bLabel, fBtn).Width) + 18;
                        var rect = new Rectangle(gx, cmdTop + 6, bw, 27);
                        g.FillRectangle(btnFill, rect);
                        g.DrawRectangle(btnBorder, rect);
                        g.DrawString(bLabel, fBtn, btnText, gx + 8, cmdTop + 10);
                        gx += bw + 6;
                    }
                    int groupW = Math.Max(70, gx - startGx - 6);
                    SizeF gSz = g.MeasureString(grp.Group, fGrp);
                    g.DrawString(grp.Group, fGrp, grpText, startGx + Math.Max(0, (groupW - gSz.Width) / 2F), ribbonHeight - 16);
                    g.DrawLine(sepPen, gx + 4, cmdTop + 6, gx + 4, ribbonHeight - 6);
                    gx += 14;
                }
            }
        }

        private static void DrawWavyUnderline(Graphics g, int x1, int x2, int y, Color color)
        {
            if (x2 <= x1 + 4) return;
            var pts = new List<PointF>();
            bool up = true;
            for (int x = x1; x <= x2; x += 4)
            {
                pts.Add(new PointF(x, up ? y - 1.6F : y + 1.6F));
                up = !up;
            }
            if (pts.Count >= 2)
            {
                using (var pen = new Pen(color, 1.9F))
                    g.DrawLines(pen, pts.ToArray());
            }
        }

        private static void DrawCalloutBadge(Graphics g, int number, int centerX, int centerY, Rectangle? highlightBox = null)
        {
            if (highlightBox.HasValue)
            {
                Rectangle hb = highlightBox.Value;
                using (var dashPen = new Pen(Color.FromArgb(215, 217, 48, 37), 2.2F) { DashStyle = DashStyle.Dash })
                {
                    g.DrawRectangle(dashPen, hb);
                }
            }

            int r = 15;
            var outer = new Rectangle(centerX - r - 2, centerY - r - 2, (r + 2) * 2, (r + 2) * 2);
            var inner = new Rectangle(centerX - r, centerY - r, r * 2, r * 2);

            using (var shadow = new SolidBrush(Color.FromArgb(75, 0, 0, 0)))
                g.FillEllipse(shadow, inner.X + 2, inner.Y + 3, inner.Width, inner.Height);

            using (var whiteRing = new SolidBrush(Color.White))
                g.FillEllipse(whiteRing, outer);

            using (var redFill = new SolidBrush(Color.FromArgb(214, 36, 28)))
                g.FillEllipse(redFill, inner);

            string s = number.ToString();
            using (var f = new Font("Segoe UI", 10.5F, FontStyle.Bold))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var wBrush = new SolidBrush(Color.White))
            {
                g.DrawString(s, f, wBrush, new RectangleF(inner.X, inner.Y + 0.5F, inner.Width, inner.Height), sf);
            }
        }

        private static void SavePng(Bitmap bmp, string dir, string fileName)
        {
            string full = Path.Combine(dir, fileName);
            bmp.Save(full, ImageFormat.Png);
        }

        // ==================== FIGURE 1: RIBBON & 4-TAB WORKSPACE OVERVIEW ====================
        private static void RenderFig01RibbonAndWorkspaceOverview(string outDir, MyanmarProofingEngine engine, MyanmarTrie trie)
        {
            var docItems = BuildSampleWordDocumentItems(engine, out string samplePara);
            using (var pane = new ProofingTaskPaneControl())
            {
                WireWordTaskPane(pane, engine, trie, samplePara);
                pane.RenderIssues(docItems);
                using (Bitmap paneBmp = CaptureWinFormsControl(pane, 445, 622))
                using (var canvas = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    ConfigureHighQualityGraphics(g);
                    g.Clear(Color.FromArgb(228, 233, 242));

                    DrawOfficeWindowChrome(
                        g,
                        Color.FromArgb(24, 90, 188),
                        "Ministry_Circular_2026.docx  —  Microsoft Word",
                        "Myanmar Spell Checker",
                        new[]
                        {
                            ("Proofing & Scan", new[] { "✓ Scan Doc", "Scan Selection", "☑ Review & Fix", "✕ Clear Marks" }),
                            ("Writing Style", new[] { "Mode: Official ▾" }),
                            ("Reference & Toolkit", new[] { "အဘိဓာန် Dictionary", "ရုံးစာ Toolkit", "+ Custom Words" }),
                            ("Language & Help", new[] { "A/က UI: EN ⇄ မြန်မာ", "User Guide ▾", "About" })
                        },
                        ribbonHeight: 116);

                    // Simulated A4 Document Page on Left (x=28..766, y=134..718)
                    var pageRect = new Rectangle(28, 134, 738, 584);
                    using (var shadow = new SolidBrush(Color.FromArgb(28, 0, 0, 0)))
                        g.FillRectangle(shadow, pageRect.X + 4, pageRect.Y + 5, pageRect.Width, pageRect.Height);
                    g.FillRectangle(Brushes.White, pageRect);
                    using (var pgBorder = new Pen(Color.FromArgb(196, 205, 218)))
                        g.DrawRectangle(pgBorder, pageRect);

                    using (var fDocHead = CreateMyanmarUiFont(13.0F, FontStyle.Bold))
                    using (var fDocSub = CreateMyanmarUiFont(10.5F, FontStyle.Bold))
                    using (var fDocBody = CreateMyanmarUiFont(10.8F, FontStyle.Regular))
                    using (var ink = new SolidBrush(Color.FromArgb(24, 32, 46)))
                    using (var muted = new SolidBrush(Color.FromArgb(88, 100, 118)))
                    {
                        g.DrawString("ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော်", fDocHead, ink, 195, 166);
                        g.DrawString("ရုံးတွင်းအမိန့်ကြော်ငြာစာ (မူကြမ်း) — အမှတ် (၁၂/၂၀၂၆)", fDocSub, muted, 185, 198);

                        // Line 1 with Red & Blue underlines
                        int lx = 66, ly = 252;
                        DrawAnnotatedMyanmarRuns(g, fDocBody, lx, ly, new (string Text, Color? Wave)[]
                        {
                            ("၁။ ပြည်ထောင်စုအစိုးရအဖွဲ့ရုံးက ", null),
                            ("အမိန့်ကြေညာစာ", Color.FromArgb(217, 48, 37)),
                            (" ", null),
                            ("တခု", Color.FromArgb(24, 100, 204)),
                            ("ကို ယနေ့ ထုတ်ပြန်သည်။", null)
                        });

                        // Line 2 with Red & Amber underlines
                        DrawAnnotatedMyanmarRuns(g, fDocBody, lx, ly + 44, new (string Text, Color? Wave)[]
                        {
                            ("၂။ ကောင်စီ", null),
                            ("ဥက္ကဌ", Color.FromArgb(217, 48, 37)),
                            ("က ", null),
                            ("ရေခဲသတ္တာ", Color.FromArgb(217, 48, 37)),
                            ("နှင့် ", null),
                            ("လုပ်ငန်းမျာ", Color.FromArgb(217, 138, 12)),
                            ("အား ", null),
                            ("စီစစ်", Color.FromArgb(217, 48, 37)),
                            (" ", null),
                            ("ပါတယ်", Color.FromArgb(24, 100, 204)),
                            ("။", null)
                        });

                        // Legend Box inside Document Page
                        var legRect = new Rectangle(66, 368, 660, 118);
                        using (var legBg = new SolidBrush(Color.FromArgb(246, 249, 255)))
                        using (var legPen = new Pen(Color.FromArgb(204, 218, 240)))
                        {
                            g.FillRectangle(legBg, legRect);
                            g.DrawRectangle(legPen, legRect);
                        }
                        using (var fLegB = new Font("Segoe UI Semibold", 9.2F, FontStyle.Bold))
                        using (var fLeg = CreateMyanmarUiFont(9.0F, FontStyle.Regular))
                        {
                            g.DrawString("Three-Color Proofing Safety System (မျဉ်းလှိုင်းအရောင် ၃ မျိုး စနစ်)", fLegB, new SolidBrush(Color.FromArgb(15, 61, 138)), 82, 380);
                            g.DrawString("• Red Wavy (အနီရောင်) : Confirmed Spelling & Homophone Errors (ဥက္ကဌ ➔ ဥက္ကဋ္ဌ, ရေခဲသတ္တာ ➔ ရေခဲသေတ္တာ)", fLeg, new SolidBrush(Color.FromArgb(185, 28, 28)), 82, 406);
                            g.DrawString("• Blue Wavy (အပြာရောင်) : Grammar, Classifier & Formal Style Rules (တခု ➔ တစ်ခု, ပါတယ် ➔ ပါသည်)", fLeg, new SolidBrush(Color.FromArgb(24, 90, 188)), 82, 431);
                            g.DrawString("• Amber Wavy (လိမ္မော်ရောင်) : Review Needed — Skipped by blind Fix All until you confirm!", fLeg, new SolidBrush(Color.FromArgb(180, 104, 8)), 82, 456);
                        }
                    }

                    // Docked Task Pane on Right (x=792, y=117)
                    using (var paneBorder = new Pen(Color.FromArgb(188, 198, 214), 1F))
                    {
                        g.DrawImage(paneBmp, 792, 117, paneBmp.Width, paneBmp.Height);
                        g.DrawRectangle(paneBorder, 791, 116, paneBmp.Width + 1, paneBmp.Height + 1);
                    }

                    // Numbered Callouts (1..5) matching OfflineUserGuideExporter Fig 1
                    DrawCalloutBadge(g, 1, 316, 48, new Rectangle(8, 64, 1224, 50));
                    DrawCalloutBadge(g, 2, 794, 170, new Rectangle(798, 152, 254, 36));
                    DrawCalloutBadge(g, 3, 794, 210, new Rectangle(798, 191, 434, 38));
                    DrawCalloutBadge(g, 4, 794, 254, new Rectangle(798, 236, 434, 36));
                    DrawCalloutBadge(g, 5, 62, 244, new Rectangle(58, 246, 650, 84));

                    SavePng(canvas, outDir, "fig01-ribbon-and-workspace-overview.png");
                }
            }
        }

        // ==================== FIGURE 2: WORD PROOFING + FLOATING QUICK-FIX POPUP ====================
        private static void RenderFig02WordLiveProofingAndQuickFix(string outDir, MyanmarProofingEngine engine, MyanmarTrie trie)
        {
            var docItems = BuildSampleWordDocumentItems(engine, out string samplePara);
            DocumentProofingItem popupTarget = docItems.FirstOrDefault(i => i.Issue.OriginalText.Contains("ဥက္ကဌ")) ?? docItems[0];

            using (var pane = new ProofingTaskPaneControl())
            using (var popup = new MyanmarQuickFixPopup(
                popupTarget,
                _ => { },
                () => { },
                () => { },
                () => { },
                duplicateCount: 3,
                onBatchReplaceAllMatching: _ => { }))
            {
                WireWordTaskPane(pane, engine, trie, samplePara);
                pane.RenderIssues(docItems);

                using (Bitmap paneBmp = CaptureWinFormsControl(pane, 450, 622))
                using (Bitmap popupBmp = CaptureStandaloneForm(popup, 464, 316))
                using (var canvas = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    ConfigureHighQualityGraphics(g);
                    g.Clear(Color.FromArgb(228, 233, 242));

                    DrawOfficeWindowChrome(
                        g,
                        Color.FromArgb(24, 90, 188),
                        "Cabinet_Report_2026.docx  —  Microsoft Word (Live Proofing & Quick-Fix)",
                        "Myanmar Spell Checker",
                        new[]
                        {
                            ("Proofing", new[] { "✓ Scan Doc", "☑ Review & Fix", "✕ Clear" }),
                            ("Style", new[] { "Official (ရုံးသုံးစာပေ) ▾" }),
                            ("Export", new[] { "≡ Audit Report ▾", "User Guide ▾" })
                        },
                        ribbonHeight: 114);

                    var pageRect = new Rectangle(24, 130, 750, 592);
                    g.FillRectangle(Brushes.White, pageRect);
                    using (var pen = new Pen(Color.FromArgb(196, 205, 218)))
                        g.DrawRectangle(pen, pageRect);

                    Rectangle ukkathaSpan;
                    using (var fTitle = CreateMyanmarUiFont(12.2F, FontStyle.Bold))
                    using (var fBody = CreateMyanmarUiFont(11.0F, FontStyle.Regular))
                    using (var ink = new SolidBrush(Color.FromArgb(22, 30, 44)))
                    {
                        g.DrawString("အစည်းအဝေး မှတ်တမ်းအကျဉ်းချုပ်", fTitle, ink, 56, 154);

                        Rectangle[] runRects = DrawAnnotatedMyanmarRuns(g, fBody, 56, 204, new (string Text, Color? Wave)[]
                        {
                            ("ကောင်စီ", null),
                            ("ဥက္ကဌ", Color.FromArgb(217, 48, 37)),
                            ("က ", null),
                            ("အမိန့်ကြေညာစာ", Color.FromArgb(217, 48, 37)),
                            (" ", null),
                            ("တခု", Color.FromArgb(24, 100, 204)),
                            ("ကို ယနေ့ စိစစ်အတည်ပြုသည်။", null)
                        });
                        ukkathaSpan = new Rectangle(runRects[1].X - 3, runRects[1].Y - 2, runRects[1].Width + 6, 31);

                        // Draw floating Quick-Fix Popup directly below 'ဥက္ကဌ'
                        int popX = 88, popY = 244;
                        using (var shadow = new SolidBrush(Color.FromArgb(46, 0, 0, 0)))
                            g.FillRectangle(shadow, popX + 5, popY + 6, popupBmp.Width, popupBmp.Height);
                        g.DrawImage(popupBmp, popX, popY, popupBmp.Width, popupBmp.Height);
                        using (var popBorder = new Pen(Color.FromArgb(182, 194, 212)))
                            g.DrawRectangle(popBorder, popX, popY, popupBmp.Width, popupBmp.Height);
                    }

                    g.DrawImage(paneBmp, 786, 115, paneBmp.Width, paneBmp.Height);
                    using (var paneBorder = new Pen(Color.FromArgb(188, 198, 214)))
                        g.DrawRectangle(paneBorder, 785, 114, paneBmp.Width + 1, paneBmp.Height + 1);

                    // Numbered Callouts (1..5) matching Fig 2
                    DrawCalloutBadge(g, 1, ukkathaSpan.X + (ukkathaSpan.Width / 2), ukkathaSpan.Y - 14, ukkathaSpan);
                    DrawCalloutBadge(g, 2, 84, 260, new Rectangle(86, 242, popupBmp.Width + 4, popupBmp.Height + 4));
                    DrawCalloutBadge(g, 3, 788, 362, new Rectangle(794, 345, 436, 34));
                    DrawCalloutBadge(g, 4, 788, 456);
                    DrawCalloutBadge(g, 5, 1002, 304, new Rectangle(1004, 302, 134, 36));

                    SavePng(canvas, outDir, "fig02-word-live-proofing-and-quickfix.png");
                }
            }
        }

        // ==================== FIGURE 3: MODELESS REVIEW & FIX STUDIO ====================
        private static void RenderFig03ReviewAndFixStudio(string outDir, MyanmarProofingEngine engine)
        {
            var docItems = BuildSampleWordDocumentItems(engine, out _);
            var entries = docItems
                .Select(it => new MyanmarSpellChecker.WordAddIn.UI.FixPreviewEntry<DocumentProofingItem>(
                    it,
                    it.FormatSectionAndParagraphLabel(),
                    it.ParagraphSnippet,
                    it.Issue))
                .ToList();

            var callbacks = new MyanmarSpellChecker.WordAddIn.UI.FixPreviewStudioCallbacks<DocumentProofingItem>
            {
                HostTag = "guide_preview_word",
                OnApplyBatch = _ => 1,
                OnIgnoreItems = _ => { },
                OnAddWordToDictionary = (_, __) => { },
                OnRefreshEntries = () => entries,
                OnUndoLastFix = () => { },
                OnDockInTaskPane = _ => { },
                OnExportReport = _ => { }
            };

            using (var dlg = new MyanmarSpellChecker.WordAddIn.UI.FixAllPreviewDialog<DocumentProofingItem>(
                entries,
                "Ministry_Circular_2026.docx (6 Findings)",
                _ => { },
                callbacks))
            using (Bitmap dlgBmp = CaptureStandaloneForm(dlg, 1140, 660))
            using (var canvas = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(canvas))
            {
                ConfigureHighQualityGraphics(g);
                g.Clear(Color.FromArgb(232, 238, 248));

                int dx = (CanvasW - dlgBmp.Width) / 2;
                int dy = (CanvasH - dlgBmp.Height) / 2;
                using (var shadow = new SolidBrush(Color.FromArgb(45, 12, 32, 68)))
                    g.FillRectangle(shadow, dx + 6, dy + 8, dlgBmp.Width, dlgBmp.Height);
                g.DrawImage(dlgBmp, dx, dy, dlgBmp.Width, dlgBmp.Height);
                using (var bPen = new Pen(Color.FromArgb(148, 168, 198), 1.5F))
                    g.DrawRectangle(bPen, dx, dy, dlgBmp.Width, dlgBmp.Height);

                // Numbered Callouts (1..5) matching Fig 3
                DrawCalloutBadge(g, 1, dx + 716, dy + 56, new Rectangle(dx + 728, dy + 40, 392, 34));
                DrawCalloutBadge(g, 2, dx + 14, dy + 98, new Rectangle(dx + 22, dy + 82, 420, 34));
                DrawCalloutBadge(g, 3, dx + 14, dy + 225);
                DrawCalloutBadge(g, 4, dx + 14, dy + dlgBmp.Height - 185);
                DrawCalloutBadge(g, 5, dx + dlgBmp.Width - 222, dy + dlgBmp.Height - 28, new Rectangle(dx + dlgBmp.Width - 210, dy + dlgBmp.Height - 48, 194, 38));

                SavePng(canvas, outDir, "fig03-review-and-fix-studio.png");
            }
        }

        // ==================== FIGURE 4: MICROSOFT EXCEL FORMULA-SAFE PROOFING ====================
        private static void RenderFig04ExcelFormulaSafeProofing(string outDir, MyanmarProofingEngine engine, MyanmarTrie trie)
        {
            var rows = new (string Code, string ItemText, string Dept, string Amount, bool HasErr)[]
            {
                ("EXP-01", "ရုံးသုံး ရေခဲသတ္တာနှင့် စက်ပစ္စည်း ဝယ်ယူခြင်း", "စီမံရေးရာဌာန", "1,500,000", true),
                ("EXP-02", "ကောင်စီဥက္ကဌ ရုံးခန်း ပြုပြင်ထိန်းသိမ်းစရိတ်", "အင်ဂျင်နီယာဌာန", "2,450,000", true),
                ("EXP-03", "သင်တန်း အစီရင် ခံစာ ပုံနှိပ်ထုတ်ဝေခြင်း", "လေ့ကျင့်ရေးဌာန", "680,000", true),
                ("EXP-04", "နှစ်တိုပရီမီယံ အာမခံကြေး ပေးသွင်းခြင်း", "ဘဏ္ဍာရေးဌာန", "920,000", false),
                ("TOTAL", "=SUM(D2:D5)   [Formula Shielded — 100% Untouched]", "စုစုပေါင်း (Kyats)", "=SUM(D2:D5)", false)
            };

            var excelIssues = new List<SpreadsheetCellIssue>();
            for (int r = 0; r < rows.Length; r++)
            {
                if (!rows[r].HasErr) continue;
                foreach (var iss in engine.CheckText(rows[r].ItemText, cacheParagraph: false))
                {
                    excelIssues.Add(new SpreadsheetCellIssue
                    {
                        WorkbookName = "Annual_Budget_2026.xlsx",
                        SheetName = "Budget2026",
                        Row = r + 2,
                        Column = 2,
                        CellSnapshotText = rows[r].ItemText,
                        Issue = iss
                    });
                }
            }

            using (var excelPane = new ExcelProofingTaskPaneControl())
            {
                excelPane.SetLexiconStats(trie.WordCount, trie.OfficialThatponCount, trie.RuleCount, trie.ReferenceCount, trie.EncyclopediaCount);
                excelPane.SetStyleMode(ProofingStyleMode.FormalOfficial);
                excelPane.SetUndoState(3, "Budget2026!B2");
                excelPane.RenderIssues(excelIssues);

                using (Bitmap paneBmp = CaptureWinFormsControl(excelPane, 445, 622))
                using (var canvas = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    ConfigureHighQualityGraphics(g);
                    g.Clear(Color.FromArgb(236, 242, 238));

                    DrawOfficeWindowChrome(
                        g,
                        Color.FromArgb(16, 124, 65),
                        "Annual_Budget_2026.xlsx  —  Microsoft Excel (Formula-Safe Proofing)",
                        "Myanmar Spell Checker",
                        new[]
                        {
                            ("Spreadsheet Scan", new[] { "✓ Scan Sheet", "Scan Workbook", "☑ Review & Fix" }),
                            ("Safety & Undo", new[] { "↶ Undo (3)", "✕ Clear Marks" }),
                            ("Accounting Toolkit", new[] { "ကျပ်သိန်း Words", "0-9 ➔ ၀-၉", "≡ Audit Report ▾" })
                        },
                        ribbonHeight: 114);

                    // Formula Bar (y=118..148)
                    var fBar = new Rectangle(12, 120, 766, 28);
                    g.FillRectangle(Brushes.White, fBar);
                    g.DrawRectangle(Pens.Silver, fBar);
                    using (var fMono = new Font("Consolas", 9.5F, FontStyle.Bold))
                    using (var green = new SolidBrush(Color.FromArgb(16, 112, 56)))
                    {
                        g.DrawString("D6   |  fx   =SUM(D2:D5)     ✓ Protected by 100% Formula Shield", fMono, green, 20, 125);
                    }

                    // Worksheet Grid (x=12..778, y=156..718)
                    var gridRect = new Rectangle(12, 156, 766, 560);
                    g.FillRectangle(Brushes.White, gridRect);
                    g.DrawRectangle(Pens.Silver, gridRect);

                    int[] colW = { 42, 86, 360, 138, 140 };
                    string[] colNames = { "", "A (Code)", "B (Description — မြန်မာစာသား)", "C (Department)", "D (Amount / Formula)" };
                    int gy = 156;
                    using (var hdrBg = new SolidBrush(Color.FromArgb(230, 242, 234)))
                    using (var gridPen = new Pen(Color.FromArgb(208, 218, 212)))
                    using (var fHdr = CreateMyanmarUiFont(8.8F, FontStyle.Bold))
                    using (var fCell = CreateMyanmarUiFont(9.4F, FontStyle.Regular))
                    using (var fCellBold = CreateMyanmarUiFont(9.4F, FontStyle.Bold))
                    {
                        int gx = 12;
                        for (int c = 0; c < colW.Length; c++)
                        {
                            var cr = new Rectangle(gx, gy, colW[c], 32);
                            g.FillRectangle(hdrBg, cr);
                            g.DrawRectangle(gridPen, cr);
                            g.DrawString(colNames[c], fHdr, Brushes.Black, gx + 6, gy + 7);
                            gx += colW[c];
                        }
                        gy += 32;

                        string[] rowHeaders = { "1", "2", "3", "4", "5", "6" };
                        // Row 1 Table Header
                        gx = 12;
                        string[] r1 = { "1", "စဉ်", "အသုံးစရိတ် အကြောင်းအရာ", "ဌာနခွဲ", "ခွင့်ပြုငွေ (ကျပ်)" };
                        for (int c = 0; c < colW.Length; c++)
                        {
                            var cr = new Rectangle(gx, gy, colW[c], 34);
                            g.FillRectangle(new SolidBrush(Color.FromArgb(244, 249, 246)), cr);
                            g.DrawRectangle(gridPen, cr);
                            g.DrawString(r1[c], fHdr, Brushes.DarkSlateGray, gx + 6, gy + 7);
                            gx += colW[c];
                        }
                        gy += 34;

                        for (int r = 0; r < rows.Length; r++)
                        {
                            gx = 12;
                            bool isFormulaRow = r == rows.Length - 1;
                            string[] vals = { rowHeaders[r + 1], rows[r].Code, rows[r].ItemText, rows[r].Dept, rows[r].Amount };
                            for (int c = 0; c < colW.Length; c++)
                            {
                                var cr = new Rectangle(gx, gy, colW[c], 44);
                                if (c == 0)
                                    g.FillRectangle(hdrBg, cr);
                                else if (isFormulaRow)
                                    g.FillRectangle(new SolidBrush(Color.FromArgb(232, 248, 238)), cr);
                                g.DrawRectangle(gridPen, cr);

                                if (c == 2 && rows[r].HasErr)
                                {
                                    Color red = Color.FromArgb(217, 48, 37);
                                    (string Text, Color? Wave)[] cellRuns = r switch
                                    {
                                        0 => new (string, Color?)[] { ("ရုံးသုံး ", null), ("ရေခဲသတ္တာ", red), ("နှင့် စက်ပစ္စည်း ဝယ်ယူခြင်း", null) },
                                        1 => new (string, Color?)[] { ("ကောင်စီ", null), ("ဥက္ကဌ", red), (" ရုံးခန်း ပြုပြင်ထိန်းသိမ်းစရိတ်", null) },
                                        _ => new (string, Color?)[] { ("သင်တန်း ", null), ("အစီရင် ခံစာ", red), (" ပုံနှိပ်ထုတ်ဝေခြင်း", null) }
                                    };
                                    DrawAnnotatedMyanmarRuns(g, fCell, gx + 8, gy + 11, cellRuns, Brushes.Black, waveOffsetY: 23);
                                }
                                else
                                {
                                    Brush b = isFormulaRow ? new SolidBrush(Color.FromArgb(14, 104, 52)) : Brushes.Black;
                                    g.DrawString(vals[c], isFormulaRow ? fCellBold : fCell, b, gx + 8, gy + 11);
                                }
                                gx += colW[c];
                            }
                            gy += 44;
                        }
                    }

                    g.DrawImage(paneBmp, 790, 116, paneBmp.Width, paneBmp.Height);
                    using (var pBorder = new Pen(Color.FromArgb(182, 204, 190)))
                        g.DrawRectangle(pBorder, 789, 115, paneBmp.Width + 1, paneBmp.Height + 1);

                    // Numbered Callouts (1..4) matching Fig 4
                    DrawCalloutBadge(g, 1, 144, 224, new Rectangle(140, 222, 356, 132));
                    DrawCalloutBadge(g, 2, 18, 398, new Rectangle(12, 398, 766, 44));
                    DrawCalloutBadge(g, 3, 792, 332, new Rectangle(798, 312, 430, 82));
                    DrawCalloutBadge(g, 4, 792, 505);

                    SavePng(canvas, outDir, "fig04-excel-formula-safe-proofing.png");
                }
            }
        }

        // ==================== FIGURE 5: POWERPOINT SLIDE & SPEAKER NOTES PROOFING ====================
        private static void RenderFig05PowerPointSlideProofing(string outDir, MyanmarProofingEngine engine, MyanmarTrie trie)
        {
            string slideTitle = "ရောဝတီတိုင်းဒေသကြီး စီမံကိန်း ရှင်းလင်းပွဲ";
            string slideBullet = "• နည်းပညာတ္ကသိုလ်နှင့် သုတေသနလုပ်ငန်းများ အရှိန်အဟုန်မြင့်တင်ရေး";
            string notesText = "Speaker Notes: အစီရင် ခံစာပါ အချက်အလက်များကို အဓိကထား ရှင်းလင်းတင်ပြရန်။";

            var pptIssues = new List<PresentationSlideIssue>();
            foreach (var (txt, kind, sName) in new[]
            {
                (slideTitle, PresentationContainerKind.SlideShape, "Title 1"),
                (slideBullet, PresentationContainerKind.SlideShape, "Content Placeholder 2"),
                (notesText, PresentationContainerKind.SpeakerNotes, "Notes Placeholder")
            })
            {
                var target = new PresentationTextTarget
                {
                    PresentationName = "Ministry_Briefing_2026.pptx",
                    SlideId = 256,
                    SlideNumber = 2,
                    SlideTitle = slideTitle,
                    ContainerKind = kind,
                    ShapeId = 10,
                    ShapeName = sName,
                    TextSnapshot = txt
                };
                foreach (var iss in engine.CheckText(txt, cacheParagraph: false))
                {
                    pptIssues.Add(new PresentationSlideIssue { Target = target, Issue = iss });
                }
            }

            using (var pptPane = new PowerPointProofingTaskPaneControl())
            {
                pptPane.SetLexiconStats(trie.WordCount, trie.OfficialThatponCount, trie.RuleCount, trie.ReferenceCount, trie.EncyclopediaCount);
                pptPane.SetStyleMode(ProofingStyleMode.FormalOfficial);
                pptPane.SetUndoState(2, "Slide 2");
                pptPane.RenderIssues(pptIssues);

                using (Bitmap paneBmp = CaptureWinFormsControl(pptPane, 445, 622))
                using (var canvas = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    ConfigureHighQualityGraphics(g);
                    g.Clear(Color.FromArgb(242, 236, 234));

                    DrawOfficeWindowChrome(
                        g,
                        Color.FromArgb(183, 71, 42),
                        "Ministry_Briefing_2026.pptx  —  Microsoft PowerPoint (Zero-Mutation Overlay)",
                        "Myanmar Spell Checker",
                        new[]
                        {
                            ("Deck Proofing", new[] { "✓ Scan Slide", "All Slides", "☑ Master", "☑ Review & Fix" }),
                            ("Slide Show Shield", new[] { "F5 Auto-Pause: ON", "↶ Undo (2)" }),
                            ("Slide Typography", new[] { "Unify Pyidaungsu", "Smart Line-Wrap ZWSP", "≡ Report ▾" })
                        },
                        ribbonHeight: 114);

                    // 16:9 Widescreen Slide Canvas (x=28..768, y=134..548)
                    var slideRect = new Rectangle(28, 134, 740, 414);
                    using (var sh = new SolidBrush(Color.FromArgb(35, 0, 0, 0)))
                        g.FillRectangle(sh, slideRect.X + 4, slideRect.Y + 5, slideRect.Width, slideRect.Height);
                    g.FillRectangle(Brushes.White, slideRect);
                    using (var accentTop = new SolidBrush(Color.FromArgb(183, 71, 42)))
                        g.FillRectangle(accentTop, slideRect.X, slideRect.Y, slideRect.Width, 12);

                    Color red = Color.FromArgb(217, 48, 37);
                    using (var fSlideTitle = CreateMyanmarUiFont(15.0F, FontStyle.Bold))
                    using (var fBullet = CreateMyanmarUiFont(11.2F, FontStyle.Regular))
                    using (var navy = new SolidBrush(Color.FromArgb(20, 40, 76)))
                    {
                        DrawAnnotatedMyanmarRuns(g, fSlideTitle, 62, 178, new (string Text, Color? Wave)[]
                        {
                            ("ရောဝတီ", red),
                            ("တိုင်းဒေသကြီး စီမံကိန်း ရှင်းလင်းပွဲ", null)
                        }, navy, waveOffsetY: 35);

                        DrawAnnotatedMyanmarRuns(g, fBullet, 62, 256, new (string Text, Color? Wave)[]
                        {
                            ("• နည်းပညာ", null),
                            ("တ္ကသိုလ်", red),
                            ("နှင့် သုတေသနလုပ်ငန်းများ အရှိန်အဟုန်", null),
                            ("မြင့်", red),
                            ("တင်ရေး", null)
                        }, Brushes.DarkSlateGray, waveOffsetY: 28);

                        g.DrawString("• F5 Slide Show Shield: Overlays & popups hide automatically during live presentation!",
                            new Font("Segoe UI Semibold", 9.6F, FontStyle.Bold),
                            new SolidBrush(Color.FromArgb(16, 118, 62)), 62, 326);
                    }

                    // Speaker Notes Pane (x=28..768, y=566..716)
                    var notesRect = new Rectangle(28, 566, 740, 150);
                    g.FillRectangle(Brushes.White, notesRect);
                    using (var nPen = new Pen(Color.FromArgb(208, 192, 186)))
                        g.DrawRectangle(nPen, notesRect);
                    using (var fNotes = CreateMyanmarUiFont(10.0F, FontStyle.Regular))
                    {
                        g.DrawString("Speaker Notes (Slide 2) — Checked Automatically Alongside Slide Shapes:",
                            new Font("Segoe UI Semibold", 9.0F, FontStyle.Bold),
                            new SolidBrush(Color.FromArgb(148, 52, 28)), 44, 580);
                        DrawAnnotatedMyanmarRuns(g, fNotes, 44, 612, new (string Text, Color? Wave)[]
                        {
                            ("Speaker Notes: ", null),
                            ("အစီရင် ခံစာ", red),
                            ("ပါ အချက်အလက်များကို အဓိကထား ရှင်းလင်းတင်ပြရန်။", null)
                        }, Brushes.Black, waveOffsetY: 25);
                    }

                    g.DrawImage(paneBmp, 790, 116, paneBmp.Width, paneBmp.Height);
                    using (var pBorder = new Pen(Color.FromArgb(210, 186, 178)))
                        g.DrawRectangle(pBorder, 789, 115, paneBmp.Width + 1, paneBmp.Height + 1);

                    // Numbered Callouts (1..4) matching Fig 5
                    DrawCalloutBadge(g, 1, 58, 176, new Rectangle(56, 172, 640, 122));
                    DrawCalloutBadge(g, 2, 48, 566, new Rectangle(28, 566, 740, 150));
                    DrawCalloutBadge(g, 3, 404, 102, new Rectangle(402, 66, 136, 31));
                    DrawCalloutBadge(g, 4, 792, 505, new Rectangle(796, 350, 432, 38));

                    SavePng(canvas, outDir, "fig05-powerpoint-slide-proofing.png");
                }
            }
        }

        // ==================== FIGURE 6: BILINGUAL DICTIONARY & ENCYCLOPEDIA ====================
        private static void RenderFig06BilingualDictionaryAndEncyclopedia(string outDir, MyanmarProofingEngine engine, MyanmarTrie trie)
        {
            using (var pane = new ProofingTaskPaneControl())
            {
                WireWordTaskPane(pane, engine, trie, "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း");
                pane.OpenDictionaryWithQuery("သတ်ပုံ");

                using (Bitmap paneBmp = CaptureWinFormsControl(pane, 485, 622))
                using (var canvas = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    ConfigureHighQualityGraphics(g);
                    g.Clear(Color.FromArgb(232, 238, 248));

                    DrawOfficeWindowChrome(
                        g,
                        Color.FromArgb(24, 90, 188),
                        "Research_Thesis_Kyaukse_University.docx  —  Offline Bilingual Dictionary & Encyclopedia",
                        "Myanmar Spell Checker",
                        new[]
                        {
                            ("4-Way Lookup", new[] { "Myanmar: သတ်ပုံ", "English: geology", "Romanized: kyaukse", "Zawgyi Auto-Convert" }),
                            ("Offline Audio & Corpus", new[] { "▶ EN Speech (SAPI)", "109,880 Bigrams", "★ Saved Bookmarks" })
                        },
                        ribbonHeight: 114);

                    // Left Reference Overview Showcase Card
                    var infoRect = new Rectangle(24, 130, 706, 590);
                    g.FillRectangle(Brushes.White, infoRect);
                    using (var pen = new Pen(Color.FromArgb(196, 210, 232)))
                        g.DrawRectangle(pen, infoRect);

                    using (var fH = new Font("Segoe UI Semibold", 13.0F, FontStyle.Bold))
                    using (var fSub = CreateMyanmarUiFont(10.0F, FontStyle.Bold))
                    using (var fB = CreateMyanmarUiFont(9.5F, FontStyle.Regular))
                    using (var brand = new SolidBrush(Color.FromArgb(15, 61, 138)))
                    using (var body = new SolidBrush(Color.FromArgb(30, 41, 59)))
                    {
                        g.DrawString("183,038-Entry Offline Lexicon, Bilingual Dictionary & Encyclopedia", fH, brand, 46, 152);
                        g.DrawString("အင်တာနက်လုံးဝမလိုသော သတ်ပုံကျမ်း၊ အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန်နှင့် စွယ်စုံဗဟုသုတကျမ်း", fSub, new SolidBrush(Color.FromArgb(24, 90, 188)), 46, 182);

                        string[] cards =
                        {
                            "✓ 96,276 Official Spellcheck Headwords (462-Page မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း + မြန်မာအဘိဓာန်)",
                            "✓ 57,064 Bilingual English ⇄ Myanmar Entries + Offline [▶ EN] Audio Pronunciation",
                            "✓ 25,001 Offline Myanmar Encyclopedia Articles (စွယ်စုံဗဟုသုတကျမ်း) + 1,929 Proverbs (စကားပုံ)",
                            "✓ 8 Academic & B2G Domains: Geology (ဘူမိဗေဒ), Public Admin, Higher Ed, Legal, Finance, Medical, Pali, IT",
                            "✓ 9,221 MIMU v9.7 / GAD / MOFA Townships, Districts, Wards & Global Cities",
                            "✓ Romanized Phonetic Lookup: Type 'mingalaba', 'thatpon', or 'kyaukse' in English letters!"
                        };

                        int cy = 228;
                        foreach (string line in cards)
                        {
                            var box = new Rectangle(46, cy, 660, 50);
                            using (var bg = new SolidBrush(Color.FromArgb(244, 248, 255)))
                            using (var bp = new Pen(Color.FromArgb(212, 226, 248)))
                            {
                                g.FillRectangle(bg, box);
                                g.DrawRectangle(bp, box);
                            }
                            g.DrawString(line, fB, body, 60, cy + 13);
                            cy += 60;
                        }
                    }

                    g.DrawImage(paneBmp, 744, 115, paneBmp.Width, paneBmp.Height);
                    using (var pBorder = new Pen(Color.FromArgb(182, 196, 218)))
                        g.DrawRectangle(pBorder, 743, 114, paneBmp.Width + 1, paneBmp.Height + 1);

                    // Numbered Callouts (1..5) matching Fig 6 (strictly top-to-bottom)
                    DrawCalloutBadge(g, 1, 746, 258, new Rectangle(752, 238, 470, 76));
                    DrawCalloutBadge(g, 2, 746, 352, new Rectangle(752, 318, 470, 70));
                    DrawCalloutBadge(g, 3, 746, 462);
                    DrawCalloutBadge(g, 4, 746, 534);
                    DrawCalloutBadge(g, 5, 746, 686);

                    SavePng(canvas, outDir, "fig06-bilingual-dictionary-and-encyclopedia.png");
                }
            }
        }

        // ==================== FIGURE 7: OFFICIAL WRITING TOOLKIT & A4 AUDIT REPORT ====================
        private static void RenderFig07OfficialWritingToolkit(string outDir, MyanmarProofingEngine engine, MyanmarTrie trie)
        {
            string cleanSample = string.Join(" ", Enumerable.Repeat(
                "ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော် ပညာရေးဝန်ကြီးဌာန ကျောက်ဆည်တက္ကသိုလ် သုတေသနစာတမ်း အစီရင်ခံစာ ဖြစ်ပါသည်။", 8));

            using (var pane = new ProofingTaskPaneControl())
            {
                WireWordTaskPane(pane, engine, trie, cleanSample);
                pane.OpenToolkitTab();

                using (Bitmap paneBmp = CaptureWinFormsControl(pane, 475, 622))
                using (var canvas = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    ConfigureHighQualityGraphics(g);
                    g.Clear(Color.FromArgb(230, 236, 245));

                    DrawOfficeWindowChrome(
                        g,
                        Color.FromArgb(24, 90, 188),
                        "Official_Budget_Letter_2026.docx  —  Official Writing Toolkit & ISO A4 Audit Report",
                        "Myanmar Spell Checker",
                        new[]
                        {
                            ("Converters", new[] { "WinInnwa/ZG ➔ Pyidaungsu", "Repair Garbled PDF", "0-9 ➔ ၀-၉ Smart" }),
                            ("Official & Financial", new[] { "ကျပ်သိန်း Words", "မြန်မာသက္ကရာဇ် Calendar", "ရုံးစာ Templates" }),
                            ("5-Format Audit", new[] { "≡ Export A4 Report (.docx / .xlsx / .pptx / .html)" })
                        },
                        ribbonHeight: 114);

                    // Simulated Printable ISO A4 Quality Audit Report Preview on Left
                    var a4 = new Rectangle(28, 130, 704, 590);
                    g.FillRectangle(Brushes.White, a4);
                    using (var pen = new Pen(Color.FromArgb(188, 202, 224)))
                        g.DrawRectangle(pen, a4);

                    using (var banner = new SolidBrush(Color.FromArgb(238, 244, 255)))
                        g.FillRectangle(banner, 48, 150, 664, 86);
                    using (var topBar = new SolidBrush(Color.FromArgb(15, 61, 138)))
                        g.FillRectangle(topBar, 48, 150, 664, 6);

                    using (var fTitle = new Font("Segoe UI Semibold", 11.8F, FontStyle.Bold))
                    using (var fMmB = CreateMyanmarUiFont(9.8F, FontStyle.Bold))
                    using (var fMm = CreateMyanmarUiFont(9.3F, FontStyle.Regular))
                    {
                        g.DrawString("Printable ISO A4 Document Quality Audit Report (.docx)", fTitle, new SolidBrush(Color.FromArgb(15, 61, 138)), 64, 164);
                        g.DrawString("အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ • Grade A+ (100% Clean Official Orthography)", fMmB, new SolidBrush(Color.FromArgb(20, 124, 64)), 64, 194);

                        g.DrawString("Built-In Official Writing Toolkit Examples (ရုံးစာကိရိယာ နမူနာများ):", fMmB, Brushes.MidnightBlue, 52, 258);

                        (string Title, string BeforeAfter)[] demos =
                        {
                            ("1. Legacy Font & Broken PDF Repair", "WinInnwa 'jynfaxmifpk'  ➔  'ပြည်ထောင်စု'   (English & Tables 100% Preserved)"),
                            ("2. Context-Smart Numeral Formatter", "'2026 ခုနှစ် COVID-19 A4'  ➔  '၂၀၂၆ ခုနှစ် COVID-19 A4'   (Codes & URLs Safe)"),
                            ("3. Banking Kyat Lakh Number-to-Words", "'Ks 1,500,000/-'  ➔  'ကျပ် ၁,၅၀၀,၀၀၀/- (ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ)'"),
                            ("4. Offline Myanmar Lunisolar Calendar", "'2026-11-24'  ➔  'သာသနာနှစ် ၂၅၇၀၊ ကောဇာ ၁၃၈၈ ခု တန်ဆောင်မုန်းလပြည့် (ဥပုသ်နေ့)'"),
                            ("5. 5-Format Audit Export (≡ Report ▾)", "Exports Printable A4 .docx, Multi-Sheet .xlsx, Executive .pptx, .csv & .html")
                        };

                        int dy = 292;
                        foreach (var d in demos)
                        {
                            var r = new Rectangle(52, dy, 656, 74);
                            using (var bg = new SolidBrush(Color.FromArgb(248, 250, 254)))
                            using (var bp = new Pen(Color.FromArgb(216, 226, 242)))
                            {
                                g.FillRectangle(bg, r);
                                g.DrawRectangle(bp, r);
                            }
                            g.DrawString(d.Title, new Font("Segoe UI Semibold", 9.2F, FontStyle.Bold), new SolidBrush(Color.FromArgb(24, 72, 152)), 66, dy + 9);
                            g.DrawString(d.BeforeAfter, fMm, Brushes.Black, 66, dy + 35);
                            dy += 82;
                        }
                    }

                    g.DrawImage(paneBmp, 750, 115, paneBmp.Width, paneBmp.Height);
                    using (var pBorder = new Pen(Color.FromArgb(182, 196, 218)))
                        g.DrawRectangle(pBorder, 749, 114, paneBmp.Width + 1, paneBmp.Height + 1);

                    // Numbered Callouts (1..4) matching Fig 7
                    DrawCalloutBadge(g, 1, 752, 340);
                    DrawCalloutBadge(g, 2, 752, 655);
                    DrawCalloutBadge(g, 3, 46, 410, new Rectangle(52, 374, 656, 74));
                    DrawCalloutBadge(g, 4, 46, 534, new Rectangle(52, 456, 656, 156));

                    SavePng(canvas, outDir, "fig07-official-writing-toolkit.png");
                }
            }
        }

        // ==================== FIGURE 8: LIVE MYANMAR UI MODE + ABOUT & ATTRIBUTION ====================
        private static void RenderFig08BilingualMyanmarUiAndAbout(string outDir, MyanmarProofingEngine engine, MyanmarTrie trie)
        {
            UiLocalizer.CurrentLanguage = UiLanguage.Myanmar;
            int totalCompiledEntries = 183038;

            using (var pane = new ProofingTaskPaneControl())
            using (var aboutDlg = new MyanmarSpellChecker.WordAddIn.UI.LicenseActivationForm(
                LicenseService.DefaultEmbeddedPublicKeyXml,
                string.Empty,
                totalCompiledEntries))
            {
                WireWordTaskPane(pane, engine, trie, "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း");
                pane.ApplyLocalization();
                pane.OpenCustomWordsTab();

                using (Bitmap paneBmp = CaptureWinFormsControl(pane, 455, 622))
                using (Bitmap aboutBmp = CaptureStandaloneForm(aboutDlg, 610, 560))
                using (var canvas = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    ConfigureHighQualityGraphics(g);
                    g.Clear(Color.FromArgb(232, 238, 248));

                    DrawOfficeWindowChrome(
                        g,
                        Color.FromArgb(24, 90, 188),
                        "မြန်မာဘာသာ စနစ်မျက်နှာပြင် (Live Myanmar UI Mode) & Open-Source Attribution",
                        "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး",
                        new[]
                        {
                            ("စာစစ်ဆေးရေး", new[] { "✓ စာတမ်းစစ်မည်", "☑ အားလုံးပြင်", "✕ မျဉ်းရှင်းမည်" }),
                            ("ဘာသာစကားနှင့် လမ်းညွှန်", new[] { "က/A UI: မြန်မာ ⇄ EN", "လမ်းညွှန်စာအုပ် ▾", "စနစ်အကြောင်း (About)" })
                        },
                        ribbonHeight: 114);

                    int ax = 58, ay = 144;
                    using (var sh = new SolidBrush(Color.FromArgb(42, 0, 0, 0)))
                        g.FillRectangle(sh, ax + 5, ay + 6, aboutBmp.Width, aboutBmp.Height);
                    g.DrawImage(aboutBmp, ax, ay, aboutBmp.Width, aboutBmp.Height);
                    using (var abPen = new Pen(Color.FromArgb(164, 182, 210)))
                        g.DrawRectangle(abPen, ax, ay, aboutBmp.Width, aboutBmp.Height);

                    g.DrawImage(paneBmp, 770, 115, paneBmp.Width, paneBmp.Height);
                    using (var pBorder = new Pen(Color.FromArgb(182, 196, 218)))
                        g.DrawRectangle(pBorder, 769, 114, paneBmp.Width + 1, paneBmp.Height + 1);

                    // Numbered Callouts (1..4) matching Fig 8
                    DrawCalloutBadge(g, 1, 770, 170, new Rectangle(778, 152, 108, 36));
                    DrawCalloutBadge(g, 2, 766, 350);
                    DrawCalloutBadge(g, 3, ax + 10, ay + 122);
                    DrawCalloutBadge(g, 4, ax + 10, ay + aboutBmp.Height - 32, new Rectangle(ax + 22, ay + aboutBmp.Height - 52, 266, 40));

                    SavePng(canvas, outDir, "fig08-bilingual-myanmar-ui-and-about.png");
                }
            }

            UiLocalizer.CurrentLanguage = UiLanguage.English;
        }

        private static Rectangle[] DrawAnnotatedMyanmarRuns(
            Graphics g,
            Font font,
            int startX,
            int y,
            (string Text, Color? Wave)[] runs,
            Brush? customBrush = null,
            int waveOffsetY = 26)
        {
            int curX = startX;
            var rects = new Rectangle[runs.Length];
            using (var defaultBrush = new SolidBrush(Color.FromArgb(24, 32, 46)))
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces })
            {
                Brush activeBrush = customBrush ?? defaultBrush;
                int idx = 0;
                foreach (var (txt, wave) in runs)
                {
                    g.DrawString(txt, font, activeBrush, curX, y, sf);
                    int w = (int)Math.Ceiling(g.MeasureString(txt, font, 2000, sf).Width);
                    int h = (int)Math.Ceiling(font.GetHeight(g));
                    rects[idx++] = new Rectangle(curX, y, w, h);
                    if (wave.HasValue && w > 6)
                    {
                        DrawWavyUnderline(g, curX + 1, curX + w - 1, y + waveOffsetY, wave.Value);
                    }
                    curX += w;
                }
            }
            return rects;
        }
    }
}
