// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Drawing;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Security;

namespace MyanmarSpellChecker.WordAddIn.UI
{
    /// <summary>
    /// High-DPI responsive Open-Source About, Author Attribution &amp; GNU GPL v3.0 License Dialog (Word Edition).
    /// Replaces legacy .lic activation prompts so users know 100% of features are free &amp; active offline,
    /// while prominently preserving Htet Yar Zar's academic and location attribution under GPLv3 Section 7(b).
    /// </summary>
    public sealed class LicenseActivationForm : Form
    {
        private const string RepoUrl = "https://github.com/Hashyz/MyanmarSpellChecker";
        private readonly Button _btnCopyRepo;
        private readonly Timer _copyFeedbackTimer;

        public LicenseValidationResult? LastValidationResult { get; private set; }

        public LicenseActivationForm(string rsaPublicKeyXml, string licenseFilePath, int loadedWordCount = 0)
        {
            _ = rsaPublicKeyXml;
            _ = licenseFilePath;

            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Text = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း — စနစ်အချက်အလက်နှင့် Open-Source လိုင်စင်";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
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

            int wrapWidth = (int)Math.Round(540 * (DeviceDpi > 0 ? DeviceDpi / 96.0 : 1.0));
            if (wrapWidth < 520) wrapWidth = 520;

            var cardPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Color.White,
                Margin = new Padding(14),
                Padding = new Padding(22, 18, 22, 18)
            };
            cardPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            cardPanel.Paint += (s, e) =>
            {
                using (var accent = new SolidBrush(Color.FromArgb(24, 90, 188)))
                {
                    e.Graphics.FillRectangle(accent, 0, 0, cardPanel.Width, 4);
                }
                using (var pen = new Pen(Color.FromArgb(214, 222, 235), 1F))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, cardPanel.Width - 1, cardPanel.Height - 1);
                }
            };

            int r = 0;

            var lblHeader = new Label
            {
                Text = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း၊ သဒ္ဒါနှင့် စွယ်စုံအဘိဓာန် (Word + Excel)",
                Font = UiFontCache.GetMyanmar(11.2F, FontStyle.Bold),
                ForeColor = Color.FromArgb(18, 48, 102),
                AutoSize = true,
                MaximumSize = new Size(wrapWidth, 0),
                Margin = new Padding(0, 2, 0, 6),
                Padding = new Padding(5, 2, 4, 2)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(lblHeader, 0, r++);

            string statsLine = loadedWordCount > 0
                ? $"✓ 100% Offline အခမဲ့ Open-Source စနစ် အသင့်အလုပ်လုပ်နေပါသည် (လိုင်စင်ဖိုင် .lic မလိုအပ်ပါ)\n" +
                  $"   သတ်ပုံကျမ်း၊ မြန်မာအဘိဓာန်၊ ဘူမိဗေဒနှင့် စွယ်စုံဗဟုသုတ စုစုပေါင်း {loadedWordCount:N0} ပုဒ် အသင့်ရှိသည်"
                : "✓ 100% Offline အခမဲ့ Open-Source စနစ် အသင့်အလုပ်လုပ်နေပါသည် (လိုင်စင်ဖိုင် .lic မလိုအပ်ပါ)";

            var lblActiveBadge = new Label
            {
                Text = statsLine,
                Font = UiFontCache.GetMyanmar(9.0F, FontStyle.Regular),
                ForeColor = Color.FromArgb(16, 102, 52),
                BackColor = Color.FromArgb(232, 247, 237),
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                MaximumSize = new Size(wrapWidth, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(10, 8, 10, 8)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(lblActiveBadge, 0, r++);

            var lblAuthorTitle = new Label
            {
                Text = "ဖန်တီးသူနှင့် ပညာရေးမှတ်တမ်း (Creator & Academic Attribution)",
                Font = UiFontCache.GetMyanmar(9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 58, 118),
                AutoSize = true,
                MaximumSize = new Size(wrapWidth, 0),
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(5, 2, 4, 2)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(lblAuthorTitle, 0, r++);

            var lblAuthorBody = new Label
            {
                Text = "• ဖန်တီးသူ (Author) : ထက်ရာဇာ — Htet Yar Zar (@Hashyz)\n" +
                       "• ပညာအရည်အချင်း : M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed)\n" +
                       "• ဌာနနှင့် တက္ကသိုလ် : ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ် (Department of Geology, Kyaukse University)\n" +
                       "• တည်နေရာဒေသ : ကျောက်ဆည်မြို့၊ မန္တလေးတိုင်းဒေသကြီး၊ မြန်မာနိုင်ငံ (Kyaukse, Myanmar)",
                Font = UiFontCache.GetMyanmar(9.0F, FontStyle.Regular),
                ForeColor = Color.FromArgb(36, 48, 66),
                BackColor = Color.FromArgb(245, 248, 255),
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                MaximumSize = new Size(wrapWidth, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(12, 9, 12, 9)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(lblAuthorBody, 0, r++);

            var lblLicenseBox = new Label
            {
                Text = "Open-Source လိုင်စင်နှင့် မူပိုင်ခွင့် သတ်မှတ်ချက် — GNU GPL v3.0 (Section 7b)\n" +
                       "ဤဆော့ဖ်ဝဲကို GNU General Public License v3.0 ဖြင့် အခမဲ့ မျှဝေထားပါသည်။ မူရင်းဖန်တီးသူအမည် (Htet Yar Zar — Kyaukse University) နှင့် NOTICE ဖိုင်ကို ဖယ်ရှားပြင်ဆင်ခြင်း၊ မိမိကိုယ်ပိုင် ပရောဂျက်အဖြစ် အမည်ပြောင်းလဲ သုံးစွဲခြင်း သို့မဟုတ် Closed-Source စီးပွားဖြစ် ရောင်းချခြင်းကို ဥပဒေအရ တားမြစ်ထားပါသည်။",
                Font = UiFontCache.GetMyanmar(8.6F, FontStyle.Regular),
                ForeColor = Color.FromArgb(66, 76, 94),
                BackColor = Color.FromArgb(250, 251, 253),
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                MaximumSize = new Size(wrapWidth, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 14),
                Padding = new Padding(10, 8, 10, 8)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(lblLicenseBox, 0, r++);

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            _btnCopyRepo = new Button
            {
                Text = "GitHub လင့်ခ် ကူးယူမည် (Copy URL)",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(24, 90, 188),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = UiFontCache.GetMyanmar(9.2F, FontStyle.Bold),
                Padding = new Padding(12, 5, 12, 5),
                Margin = new Padding(0, 0, 10, 0),
                Cursor = Cursors.Hand
            };
            _btnCopyRepo.FlatAppearance.BorderColor = Color.FromArgb(18, 72, 158);
            _btnCopyRepo.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(RepoUrl);
                    _btnCopyRepo.Text = "✓ GitHub လင့်ခ် ကူးယူပြီးပါပြီ";
                    _btnCopyRepo.BackColor = Color.FromArgb(22, 128, 62);
                    _copyFeedbackTimer.Stop();
                    _copyFeedbackTimer.Start();
                }
                catch { }
            };

            var btnClose = new Button
            {
                Text = "ပိတ်မည် (Esc)",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(242, 244, 248),
                ForeColor = Color.FromArgb(50, 60, 76),
                Font = UiFontCache.GetMyanmar(9.2F, FontStyle.Regular),
                Padding = new Padding(14, 5, 14, 5),
                Margin = new Padding(0),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(210, 216, 228);
            btnClose.Click += (s, e) => Close();

            btnRow.Controls.Add(_btnCopyRepo);
            btnRow.Controls.Add(btnClose);
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(btnRow, 0, r++);

            Controls.Add(cardPanel);
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
                _btnCopyRepo.Text = "GitHub လင့်ခ် ကူးယူမည် (Copy URL)";
                _btnCopyRepo.BackColor = Color.FromArgb(24, 90, 188);
            }
        }
    }
}
