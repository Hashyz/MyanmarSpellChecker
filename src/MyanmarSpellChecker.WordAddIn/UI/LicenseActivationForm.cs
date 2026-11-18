// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Security;

namespace MyanmarSpellChecker.WordAddIn.UI
{
    /// <summary>
    /// High-DPI responsive, resizable &amp; scrollable Open-Source About, Author Attribution &amp; GNU GPL v3.0 License Dialog (Word Edition).
    /// Fits comfortably on small laptop screens (720p / 1366x768 / 125-150% DPI) with a scrollable body and pinned footer buttons,
    /// while prominently preserving Htet Yar Zar's academic and location attribution under GPLv3 Section 7(b).
    /// </summary>
    public sealed class LicenseActivationForm : Form
    {
        private const string RepoUrl = "https://github.com/Hashyz/MyanmarSpellChecker";
        private readonly Panel _scrollHost;
        private readonly TableLayoutPanel _cardPanel;
        private readonly Panel _footerBar;
        private readonly List<Control> _wrappableLabels = new List<Control>(6);
        private readonly Button _btnCopyRepo;
        private readonly Timer _copyFeedbackTimer;
        private readonly int _dpi;

        public LicenseValidationResult? LastValidationResult { get; private set; }

        private int S(int logicalPx) => UiFontCache.ScaleLogicalPxForDpi(_dpi, logicalPx);

        public LicenseActivationForm(string rsaPublicKeyXml, string licenseFilePath, int loadedWordCount = 0)
        {
            _ = rsaPublicKeyXml;
            _ = licenseFilePath;
            _dpi = UiFontCache.GetEffectiveDpi(this);

            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Text = UiLocalizer.Pick(
                "Myanmar Spell Checker — About & Open-Source License",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း — စနစ်အချက်အလက်နှင့် Open-Source လိုင်စင်");
            AutoSize = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(244, 247, 252);
            Font = UiFontCache.GetMyanmar(9.5F, FontStyle.Regular);
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    Close();
            };

            _copyFeedbackTimer = new Timer { Interval = 2000 };
            _copyFeedbackTimer.Tick += OnCopyFeedbackTimerTick;

            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int minW = Math.Min(S(400), Math.Max(S(310), workArea.Width - S(28)));
            int minH = Math.Min(S(300), Math.Max(S(240), workArea.Height - S(48)));
            MinimumSize = new Size(minW, minH);

            int targetClientW = Math.Min(S(580), Math.Max(minW, workArea.Width - S(48)));
            int initWrapWidth = Math.Max(S(240), targetClientW - S(68));

            // Pinned Sticky Bottom Action Bar (never scrolls off-screen on small laptops!)
            _footerBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = S(52),
                BackColor = Color.White,
                Padding = new Padding(S(18), S(8), S(18), S(8))
            };
            _footerBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(216, 224, 236), 1F))
                {
                    e.Graphics.DrawLine(pen, 0, 0, _footerBar.Width, 0);
                }
            };

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                Margin = new Padding(0)
            };

            _btnCopyRepo = new Button
            {
                Text = UiLocalizer.Pick("Copy GitHub URL", "GitHub လင့်ခ် ကူးယူမည်"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(24, 90, 188),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = UiFontCache.GetMyanmar(9.2F, FontStyle.Bold),
                Padding = new Padding(S(10), S(4), S(10), S(4)),
                Margin = new Padding(0, 0, S(8), 0),
                Cursor = Cursors.Hand
            };
            _btnCopyRepo.FlatAppearance.BorderColor = Color.FromArgb(18, 72, 158);
            _btnCopyRepo.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(RepoUrl);
                    _btnCopyRepo.Text = UiLocalizer.Pick("✓ GitHub URL Copied", "✓ GitHub လင့်ခ် ကူးယူပြီးပါပြီ");
                    _btnCopyRepo.BackColor = Color.FromArgb(22, 128, 62);
                    _copyFeedbackTimer.Stop();
                    _copyFeedbackTimer.Start();
                }
                catch { }
            };

            var btnClose = new Button
            {
                Text = UiLocalizer.Pick("Close (Esc)", "ပိတ်မည် (Esc)"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(242, 244, 248),
                ForeColor = Color.FromArgb(50, 60, 76),
                Font = UiFontCache.GetMyanmar(9.2F, FontStyle.Regular),
                Padding = new Padding(S(12), S(4), S(12), S(4)),
                Margin = new Padding(0),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(210, 216, 228);
            btnClose.Click += (s, e) => Close();

            var btnHtmlGuide = new Button
            {
                Text = UiLocalizer.Pick("User Guide (HTML)", "လမ်းညွှန် (HTML)"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(16, 118, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = UiFontCache.GetMyanmar(9.0F, FontStyle.Bold),
                Padding = new Padding(S(9), S(4), S(9), S(4)),
                Margin = new Padding(0, 0, S(6), 0),
                Cursor = Cursors.Hand
            };
            btnHtmlGuide.FlatAppearance.BorderColor = Color.FromArgb(12, 96, 50);
            btnHtmlGuide.Click += (s, e) =>
            {
                try { OfflineUserGuideExporter.OpenInteractiveHtmlGuide(); } catch { }
            };

            var btnWordGuide = new Button
            {
                Text = UiLocalizer.Pick("A4 Manual (.docx)", "A4 စာအုပ် (.docx)"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(234, 243, 255),
                ForeColor = Color.FromArgb(18, 72, 162),
                FlatStyle = FlatStyle.Flat,
                Font = UiFontCache.GetMyanmar(9.0F, FontStyle.Bold),
                Padding = new Padding(S(9), S(4), S(9), S(4)),
                Margin = new Padding(0, 0, S(8), 0),
                Cursor = Cursors.Hand
            };
            btnWordGuide.FlatAppearance.BorderColor = Color.FromArgb(182, 206, 245);
            btnWordGuide.Click += (s, e) =>
            {
                try { OfflineUserGuideExporter.OpenPrintableWordGuide(); } catch { }
            };

            btnRow.Controls.Add(btnHtmlGuide);
            btnRow.Controls.Add(btnWordGuide);
            btnRow.Controls.Add(_btnCopyRepo);
            btnRow.Controls.Add(btnClose);
            _footerBar.Controls.Add(btnRow);

            // Scrollable Content Host (smooth mouse-wheel & scrollbar support on small screens)
            _scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(244, 247, 252),
                Padding = new Padding(S(12), S(12), S(12), S(10))
            };

            _cardPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Color.White,
                Margin = new Padding(0),
                Padding = new Padding(S(18), S(14), S(18), S(14))
            };
            _cardPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _cardPanel.Paint += (s, e) =>
            {
                using (var accent = new SolidBrush(Color.FromArgb(24, 90, 188)))
                {
                    e.Graphics.FillRectangle(accent, 0, 0, _cardPanel.Width, Math.Max(3, S(4)));
                }
                using (var pen = new Pen(Color.FromArgb(214, 222, 235), 1F))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _cardPanel.Width - 1, _cardPanel.Height - 1);
                }
            };

            int r = 0;

            var lblHeader = new Label
            {
                Text = UiLocalizer.Pick(
                    "Myanmar Spell Checker, Grammar & Encyclopedia Suite (Word • Excel • PowerPoint)",
                    "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း၊ သဒ္ဒါနှင့် စွယ်စုံအဘိဓာန် (Word • Excel • PowerPoint)"),
                Font = UiFontCache.GetMyanmar(10.8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(18, 48, 102),
                AutoSize = true,
                MaximumSize = new Size(initWrapWidth, 0),
                Margin = new Padding(0, S(2), 0, S(6)),
                Padding = new Padding(S(2), S(2), S(2), S(2))
            };
            _wrappableLabels.Add(lblHeader);
            _cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _cardPanel.Controls.Add(lblHeader, 0, r++);

            string statsLine = loadedWordCount > 0
                ? UiLocalizer.Pick(
                    $"✓ 100% Free & Offline Open-Source Suite Active (No .lic license file required)\n" +
                    $"   {loadedWordCount:N0} Official Thatpon, Dictionary, Geology & Encyclopedia entries loaded",
                    $"✓ 100% Offline အခမဲ့ Open-Source စနစ် အသင့်အလုပ်လုပ်နေပါသည် (လိုင်စင်ဖိုင် .lic မလိုအပ်ပါ)\n" +
                    $"   သတ်ပုံကျမ်း၊ မြန်မာအဘိဓာန်၊ ဘူမိဗေဒနှင့် စွယ်စုံဗဟုသုတ စုစုပေါင်း {loadedWordCount:N0} ပုဒ် အသင့်ရှိသည်")
                : UiLocalizer.Pick(
                    "✓ 100% Free & Offline Open-Source Suite Active (No .lic license file required)",
                    "✓ 100% Offline အခမဲ့ Open-Source စနစ် အသင့်အလုပ်လုပ်နေပါသည် (လိုင်စင်ဖိုင် .lic မလိုအပ်ပါ)");

            var lblActiveBadge = new Label
            {
                Text = statsLine,
                Font = UiFontCache.GetMyanmar(9.0F, FontStyle.Regular),
                ForeColor = Color.FromArgb(16, 102, 52),
                BackColor = Color.FromArgb(232, 247, 237),
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                MaximumSize = new Size(initWrapWidth, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, S(10)),
                Padding = new Padding(S(9), S(7), S(9), S(7))
            };
            _wrappableLabels.Add(lblActiveBadge);
            _cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _cardPanel.Controls.Add(lblActiveBadge, 0, r++);

            var lblAuthorTitle = new Label
            {
                Text = UiLocalizer.Pick(
                    "Creator, Dedication & Collaborators",
                    "ဖန်တီးသူ၊ ဂုဏ်ပြုမှတ်တမ်းနှင့် ပူးပေါင်းပါဝင်သူများ (Creator, Dedication & Collaborators)"),
                Font = UiFontCache.GetMyanmar(9.4F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 58, 118),
                AutoSize = true,
                MaximumSize = new Size(initWrapWidth, 0),
                Margin = new Padding(0, 0, 0, S(4)),
                Padding = new Padding(S(2), S(1), S(2), S(1))
            };
            _wrappableLabels.Add(lblAuthorTitle);
            _cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _cardPanel.Controls.Add(lblAuthorTitle, 0, r++);

            var lblAuthorBody = new Label
            {
                Text = UiLocalizer.Pick(
                    "• Original Creator : Htet Yar Zar (ထက်ရာဇာ) (@Hashyz)\n" +
                    "• Academic Standing : M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed)\n" +
                    "• Department & University : Department of Geology, Kyaukse University (ကျောက်ဆည်တက္ကသိုလ်)\n" +
                    "• Location : Kyaukse, Mandalay Region, Myanmar\n" +
                    "• Special Dedication (Original Idea) : Father U Moe Kyaw Thu (ဖခင် ဦးမိုးကျော်သူ)\n" +
                    "• Project Collaborators : Hein Htet Zaw (ဟိန်းထက်ဇော်), Thu Reain Khant (သူရိန်ခန့်)",
                    "• ဖန်တီးသူ (Original Creator) : ထက်ရာဇာ — Htet Yar Zar (@Hashyz)\n" +
                    "• ပညာအရည်အချင်း : M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed)\n" +
                    "• ဌာနနှင့် တက္ကသိုလ် : ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ် (Department of Geology, Kyaukse University)\n" +
                    "• တည်နေရာဒေသ : ကျောက်ဆည်မြို့၊ မန္တလေးတိုင်းဒေသကြီး၊ မြန်မာနိုင်ငံ (Kyaukse, Myanmar)\n" +
                    "• မူလစိတ်ကူးလမ်းညွှန်ပေးသူ (Original Idea) : ဖခင် ဦးမိုးကျော်သူ (Father U Moe Kyaw Thu)\n" +
                    "• ပူးပေါင်းပါဝင်သူများ (Collaborators) : ဟိန်းထက်ဇော် (Hein Htet Zaw)၊ သူရိန်ခန့် (Thu Reain Khant)"),
                Font = UiFontCache.GetMyanmar(8.9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(36, 48, 66),
                BackColor = Color.FromArgb(245, 248, 255),
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                MaximumSize = new Size(initWrapWidth, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, S(10)),
                Padding = new Padding(S(10), S(8), S(10), S(8))
            };
            _wrappableLabels.Add(lblAuthorBody);
            _cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _cardPanel.Controls.Add(lblAuthorBody, 0, r++);

            var lblLicenseBox = new Label
            {
                Text = UiLocalizer.Pick(
                    "Open-Source License & Attribution Notice — GNU GPL v3.0 (Section 7b)\n" +
                    "Released freely under the GNU General Public License v3.0. Removing or altering the original creator attribution (Htet Yar Zar — Kyaukse University) or NOTICE file, rebranding as your own project, or redistributing as closed-source proprietary software is strictly prohibited.",
                    "Open-Source လိုင်စင်နှင့် မူပိုင်ခွင့် သတ်မှတ်ချက် — GNU GPL v3.0 (Section 7b)\n" +
                    "ဤဆော့ဖ်ဝဲကို GNU General Public License v3.0 ဖြင့် အခမဲ့ မျှဝေထားပါသည်။ မူရင်းဖန်တီးသူအမည် (Htet Yar Zar — Kyaukse University) နှင့် NOTICE ဖိုင်ကို ဖယ်ရှားပြင်ဆင်ခြင်း၊ မိမိကိုယ်ပိုင် ပရောဂျက်အဖြစ် အမည်ပြောင်းလဲ သုံးစွဲခြင်း သို့မဟုတ် Closed-Source စီးပွားဖြစ် ရောင်းချခြင်းကို ဥပဒေအရ တားမြစ်ထားပါသည်။"),
                Font = UiFontCache.GetMyanmar(8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(66, 76, 94),
                BackColor = Color.FromArgb(250, 251, 253),
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                MaximumSize = new Size(initWrapWidth, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(S(9), S(7), S(9), S(7))
            };
            _wrappableLabels.Add(lblLicenseBox);
            _cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _cardPanel.Controls.Add(lblLicenseBox, 0, r++);

            _scrollHost.Controls.Add(_cardPanel);
            _scrollHost.ClientSizeChanged += (s, e) => ReflowWrapWidths();

            // Dock order: Fill added first, Bottom added second so _scrollHost fills strictly above _footerBar
            Controls.Add(_scrollHost);
            Controls.Add(_footerBar);

            UiFontCache.DisableMnemonicsRecursive(this);
            FitInitialBoundsToScreen(workArea, targetClientW);
        }

        private void FitInitialBoundsToScreen(Rectangle workArea, int targetClientW)
        {
            ClientSize = new Size(targetClientW, S(480));
            ReflowWrapWidths();
            _cardPanel.PerformLayout();

            int desiredContentH = _cardPanel.PreferredSize.Height + _scrollHost.Padding.Vertical + _footerBar.Height + S(6);
            int maxAllowedClientH = Math.Max(S(240), (int)(workArea.Height * 0.84) - (Height - ClientSize.Height));
            int finalClientH = Math.Max(S(260), Math.Min(desiredContentH, maxAllowedClientH));

            ClientSize = new Size(targetClientW, finalClientH);
            ReflowWrapWidths();

            int posX = workArea.Left + Math.Max(S(10), (workArea.Width - Width) / 2);
            int posY = workArea.Top + Math.Max(S(10), (workArea.Height - Height) / 2);
            Location = new Point(posX, posY);
        }

        private void ReflowWrapWidths()
        {
            if (_scrollHost == null || _cardPanel == null || _scrollHost.ClientSize.Width <= 60)
                return;

            int availCardW = Math.Max(S(220), _scrollHost.ClientSize.Width - _scrollHost.Padding.Horizontal);
            int wrapW = Math.Max(S(180), availCardW - _cardPanel.Padding.Horizontal - S(4));

            _cardPanel.SuspendLayout();
            try
            {
                foreach (Control lbl in _wrappableLabels)
                {
                    if (lbl.MaximumSize.Width != wrapW)
                    {
                        lbl.MaximumSize = new Size(wrapW, 0);
                    }
                }
            }
            finally
            {
                _cardPanel.ResumeLayout(true);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ReflowWrapWidths();
            _scrollHost.Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Dispose();
            base.OnFormClosed(e);
        }

        private void OnCopyFeedbackTimerTick(object? sender, EventArgs e)
        {
            _copyFeedbackTimer.Stop();
            if (!_btnCopyRepo.IsDisposed)
            {
                _btnCopyRepo.Text = UiLocalizer.Pick("Copy GitHub URL", "GitHub လင့်ခ် ကူးယူမည်");
                _btnCopyRepo.BackColor = Color.FromArgb(24, 90, 188);
            }
        }
    }
}
