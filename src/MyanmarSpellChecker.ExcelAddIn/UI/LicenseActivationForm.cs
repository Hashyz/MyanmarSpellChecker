using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Security;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    /// <summary>
    /// High-DPI responsive Offline Enterprise / Government (B2G) License Activation &amp; Hardware ID Dialog (Excel Edition).
    /// </summary>
    public sealed class LicenseActivationForm : Form
    {
        private readonly string _rsaPublicKeyXml;
        private readonly string _licenseFilePath;
        private readonly TextBox _txtMachineId;
        private readonly Label _lblStatus;
        private readonly Button _btnCopy;
        private readonly Timer _copyFeedbackTimer;

        public LicenseValidationResult? LastValidationResult { get; private set; }

        public LicenseActivationForm(string rsaPublicKeyXml, string licenseFilePath, int loadedWordCount = 0)
        {
            _rsaPublicKeyXml = rsaPublicKeyXml;
            _licenseFilePath = licenseFilePath;

            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Text = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (Excel) — Offline လိုင်စင်နှင့် စနစ်အချက်အလက်";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(560, 0);
            MaximumSize = new Size(720, 800);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(246, 248, 252);
            Font = UiFontCache.GetMyanmar(9.5F, FontStyle.Regular);
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    Close();
            };

            _copyFeedbackTimer = new Timer { Interval = 1800 };
            _copyFeedbackTimer.Tick += OnCopyFeedbackTimerTick;

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
                using (var accent = new SolidBrush(Color.FromArgb(16, 124, 65)))
                {
                    e.Graphics.FillRectangle(accent, 0, 0, cardPanel.Width, 4);
                }
                using (var pen = new Pen(Color.FromArgb(215, 222, 234), 1F))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, cardPanel.Width - 1, cardPanel.Height - 1);
                }
            };

            int r = 0;

            var lblHeader = new Label
            {
                Text = "ရုံးသုံး မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (Word + Excel Suite)",
                Font = UiFontCache.GetMyanmar(11.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 88, 48),
                AutoSize = true,
                MaximumSize = new Size(510, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 4),
                Padding = new Padding(0, 2, 0, 2)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(lblHeader, 0, r++);

            string telemetryText = loadedWordCount > 0
                ? $"✓ 100% Offline အင်တာနက်မလိုသော လုံခြုံရေးစနစ်  •  ဝေါဟာရနှင့် စွယ်စုံဗဟုသုတ {loadedWordCount:N0} ပုဒ် အသင့်ရှိသည်"
                : "✓ 100% Offline အင်တာနက်မလိုသော လုံခြုံရေးစနစ်  •  RSA-2048 + AES-256 အဘိဓာန် ကာကွယ်မှု";

            var lblTelemetry = new Label
            {
                Text = telemetryText,
                Font = UiFontCache.GetMyanmar(8.8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(75, 88, 108),
                AutoSize = true,
                MaximumSize = new Size(510, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(0, 1, 0, 2)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(lblTelemetry, 0, r++);

            var lblMachine = new Label
            {
                Text = "ဤကွန်ပျူတာ၏ စက်အမှတ် (Hardware Machine ID):",
                Font = UiFontCache.GetMyanmar(9.2F, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 50, 68),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(0, 2, 0, 2)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(lblMachine, 0, r++);

            var idGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 14)
            };
            idGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
            idGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            idGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtMachineId = new TextBox
            {
                Text = HardwareFingerprint.GetMachineId(),
                ReadOnly = true,
                TabStop = false,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 247, 250),
                ForeColor = Color.FromArgb(25, 35, 50),
                Font = UiFontCache.GetMonospace(11F, FontStyle.Bold),
                Margin = new Padding(0, 3, 8, 3)
            };

            _btnCopy = new Button
            {
                Text = "စက်အမှတ် ကူးယူမည် (Copy)",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(232, 245, 237),
                ForeColor = Color.FromArgb(16, 102, 54),
                Font = UiFontCache.GetMyanmar(9F, FontStyle.Bold),
                Padding = new Padding(8, 4, 8, 4),
                Margin = new Padding(0),
                Cursor = Cursors.Hand
            };
            _btnCopy.FlatAppearance.BorderColor = Color.FromArgb(176, 218, 192);
            _btnCopy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_txtMachineId.Text);
                    _btnCopy.Text = "✓ ကူးယူပြီးပါပြီ";
                    _copyFeedbackTimer.Stop();
                    _copyFeedbackTimer.Start();
                }
                catch { }
            };

            idGrid.Controls.Add(_txtMachineId, 0, 0);
            idGrid.Controls.Add(_btnCopy, 1, 0);
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(idGrid, 0, r++);

            _lblStatus = new Label
            {
                Text = "လိုင်စင်ဖိုင် (.lic) ကို Word နှင့် Excel နှစ်မျိုးလုံးတွင် တူညီစွာ မျှဝေအသုံးပြုနိုင်ပါသည်။",
                Font = UiFontCache.GetMyanmar(9.2F, FontStyle.Regular),
                ForeColor = Color.FromArgb(20, 82, 46),
                BackColor = Color.FromArgb(236, 248, 240),
                AutoSize = true,
                MaximumSize = new Size(510, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 16),
                Padding = new Padding(10, 8, 10, 8)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(_lblStatus, 0, r++);

            var btnGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66F));
            btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            btnGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnImport = new Button
            {
                Text = "လိုင်စင်ဖိုင် (.lic) ထည့်သွင်းအတည်ပြုမည်...",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 124, 65),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = UiFontCache.GetMyanmar(9.5F, FontStyle.Bold),
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(0, 0, 10, 0),
                Cursor = Cursors.Hand
            };
            btnImport.FlatAppearance.BorderColor = Color.FromArgb(14, 104, 54);
            btnImport.Click += OnImportLicenseClicked;

            var btnClose = new Button
            {
                Text = "ပိတ်မည် (Esc)",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(242, 244, 248),
                ForeColor = Color.FromArgb(55, 65, 80),
                Font = UiFontCache.GetMyanmar(9.2F, FontStyle.Regular),
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(0),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(212, 218, 228);
            btnClose.Click += (s, e) => Close();

            btnGrid.Controls.Add(btnImport, 0, 0);
            btnGrid.Controls.Add(btnClose, 1, 0);
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(btnGrid, 0, r++);

            var lblAuthorCard = new Label
            {
                Text = "ဖန်တီးသူနှင့် ပညာရေးမှတ်တမ်း — ထက်ရာဇာ (Htet Yar Zar)\n" +
                       "M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed) • ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ် (Kyaukse University)\n" +
                       "ကျောက်ဆည်မြို့၊ မန္တလေးတိုင်းဒေသကြီး၊ မြန်မာနိုင်ငံ  •  GNU GPL v3.0 (Section 7b Attribution Required)",
                Font = UiFontCache.GetMyanmar(8.3F, FontStyle.Regular),
                ForeColor = Color.FromArgb(44, 72, 56),
                BackColor = Color.FromArgb(244, 250, 246),
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                MaximumSize = new Size(510, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 14, 0, 0),
                Padding = new Padding(10, 7, 10, 7)
            };
            cardPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardPanel.Controls.Add(lblAuthorCard, 0, r++);

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 1,
                BackColor = Color.FromArgb(246, 248, 252)
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.Controls.Add(cardPanel, 0, 0);

            Controls.Add(outer);
            RefreshCurrentStatus();
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
            if (!_btnCopy.IsDisposed)
            {
                _btnCopy.Text = "စက်အမှတ် ကူးယူမည် (Copy)";
            }
        }

        private void RefreshCurrentStatus()
        {
            if (File.Exists(_licenseFilePath))
            {
                string json = File.ReadAllText(_licenseFilePath);
                var res = LicenseService.ValidateLicenseJson(json, _rsaPublicKeyXml);
                LastValidationResult = res;
                if (res.IsValid)
                {
                    _lblStatus.Text = $"✓ {res.StatusMessage}";
                    _lblStatus.ForeColor = Color.FromArgb(18, 118, 52);
                    _lblStatus.BackColor = Color.FromArgb(232, 247, 237);
                    return;
                }
            }

            _lblStatus.Text = "✓ စံသတ်ပုံကျမ်း Offline စနစ် (Word + Excel) အသင့်အလုပ်လုပ်နေပါသည်။";
            _lblStatus.ForeColor = Color.FromArgb(20, 82, 46);
            _lblStatus.BackColor = Color.FromArgb(236, 248, 240);
        }

        private void OnImportLicenseClicked(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "လိုင်စင်ဖိုင် (.lic) ရွေးချယ်ရန်";
                ofd.Filter = "License Files (*.lic)|*.lic|All Files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK)
                    return;

                string json = File.ReadAllText(ofd.FileName);
                var result = LicenseService.ValidateLicenseJson(json, _rsaPublicKeyXml);
                LastValidationResult = result;

                if (result.IsValid)
                {
                    string? dir = Path.GetDirectoryName(_licenseFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllText(_licenseFilePath, json);
                    _lblStatus.Text = $"✓ {result.StatusMessage}";
                    _lblStatus.ForeColor = Color.FromArgb(18, 118, 52);
                    _lblStatus.BackColor = Color.FromArgb(232, 247, 237);
                    DialogResult = DialogResult.OK;
                }
                else
                {
                    _lblStatus.Text = $"⚠ {result.StatusMessage}";
                    _lblStatus.ForeColor = Color.FromArgb(175, 28, 28);
                    _lblStatus.BackColor = Color.FromArgb(253, 236, 234);
                }
            }
        }
    }
}

