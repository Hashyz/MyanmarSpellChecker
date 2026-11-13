using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.WordAddIn.Interop;

namespace MyanmarSpellChecker.WordAddIn.UI
{
    public sealed partial class ProofingTaskPaneControl
    {
        // ================= FLAGSHIP V11 DOCUMENT TOOLKIT & OFFICIAL WRITING SUITE =================

        private void RenderToolkitWorkspaceView()
        {
            if (_latestQualityReport == null && RequestRefreshQualityReport != null)
            {
                try
                {
                    _latestQualityReport = RequestRefreshQualityReport.Invoke();
                    if (_latestQualityReport?.OfficialLetterAudit != null)
                    {
                        _latestOfficialAudit = _latestQualityReport.OfficialLetterAudit;
                    }
                }
                catch { }
            }

            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            // ------------------------------------------------------------------
            // CARD 1: Live 0-100% Document Quality Scorecard & Audit Certificate
            // ------------------------------------------------------------------
            DocumentQualityReport report = _latestQualityReport ?? new DocumentQualityReport();
            Color scoreAccent = report.OverallScore >= 90
                ? Color.FromArgb(20, 128, 66)
                : (report.OverallScore >= 75 ? Color.FromArgb(24, 90, 188) : Color.FromArgb(196, 102, 14));

            var qualityCard = CreateStyledCardContainer(cardWidth, scoreAccent, isSelectableItem: null);
            int qRow = 0;

            var lblQHeader = new Label
            {
                Text = UiLocalizer.Pick(
                    "1. Document Quality Scorecard & Audit",
                    "၁။ စာတမ်းအရည်အသွေး စစ်ဆေးချက် (Quality Scorecard)"),
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(18, 48, 104),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(2, 1, 2, 1)
            };
            qualityCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            qualityCard.Controls.Add(lblQHeader, 0, qRow++);

            string scoreDigits = UiLocalizer.IsEnglish
                ? report.OverallScore.ToString()
                : MyanmarTraditionalCalendar.ToMyanmarDigits(report.OverallScore);
            var lblScoreBadge = new Label
            {
                Text = UiLocalizer.Pick(
                    $"Score {scoreDigits}%  •  {report.LocalizedGradeLabel}",
                    $"ရမှတ် {scoreDigits}%  •  {report.GradeLabel}"),
                Font = _fontCardWord,
                ForeColor = scoreAccent,
                BackColor = report.OverallScore >= 90
                    ? Color.FromArgb(234, 248, 239)
                    : Color.FromArgb(240, 246, 255),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(8, 5, 8, 5)
            };
            qualityCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            qualityCard.Controls.Add(lblScoreBadge, 0, qRow++);

            int estMins = (int)Math.Max(0, Math.Round(report.EstimatedReadingMinutes));
            string readMinText = UiLocalizer.IsEnglish
                ? (report.EstimatedReadingMinutes < 1.0 && report.MyanmarSyllableCount > 0 ? "< 1 min" : $"~{estMins} min")
                : (report.EstimatedReadingMinutes < 1.0 && report.MyanmarSyllableCount > 0
                    ? "၁ မိနစ်အောက်"
                    : $"{MyanmarTraditionalCalendar.ToMyanmarDigits(estMins)} မိနစ်ခန့်");
            var lblQMetrics = new Label
            {
                Text = UiLocalizer.IsEnglish
                    ? $"• Unicode Purity: {report.UnicodePurityPercent:0.#}%   |   Formal Register: {report.FormalRegisterPercent:0.#}%\n" +
                      $"• Syllables: {report.MyanmarSyllableCount:N0}  •  Words: {report.WordCount:N0}  •  Read Time: {readMinText}\n" +
                      $"• Findings: Spelling ({report.SpellingIssueCount}), Grammar/Style ({report.GrammarIssueCount}), Encoding ({report.EncodingIssueCount})"
                    : $"• ယူနီကုဒ် သန့်စင်မှု: {report.UnicodePurityPercent:0.#}%   |   ရုံးသုံးဟန်: {report.FormalRegisterPercent:0.#}%\n" +
                      $"• ဝဏ္ဏ: {MyanmarTraditionalCalendar.ToMyanmarDigits(report.MyanmarSyllableCount)} လုံး  •  စကားလုံး: {MyanmarTraditionalCalendar.ToMyanmarDigits(report.WordCount)}  •  ဖတ်ချိန်: {readMinText}\n" +
                      $"• တွေ့ရှိချက်: စာလုံးပေါင်း ({MyanmarTraditionalCalendar.ToMyanmarDigits(report.SpellingIssueCount)})၊ သဒ္ဒါ/ဟန် ({MyanmarTraditionalCalendar.ToMyanmarDigits(report.GrammarIssueCount)})၊ ဖောင့် ({MyanmarTraditionalCalendar.ToMyanmarDigits(report.EncodingIssueCount)})",
                Font = _fontSmall,
                ForeColor = Color.FromArgb(48, 58, 74),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(2, 1, 2, 1)
            };
            qualityCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            qualityCard.Controls.Add(lblQMetrics, 0, qRow++);

            var qBtnGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            qBtnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            qBtnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            qBtnGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnRefreshScore = CreatePillButton(
                UiLocalizer.Pick("⟳ Refresh", "⟳ ပြန်တွက်မည်"),
                Color.FromArgb(240, 244, 250), Color.FromArgb(35, 55, 88), Color.FromArgb(210, 220, 236), _fontSmallBold);
            btnRefreshScore.Click += (s, e) =>
            {
                if (RequestRefreshQualityReport != null)
                {
                    _latestQualityReport = RequestRefreshQualityReport.Invoke();
                    if (_latestQualityReport?.OfficialLetterAudit != null)
                        _latestOfficialAudit = _latestQualityReport.OfficialLetterAudit;
                    RenderToolkitWorkspaceView();
                    ShowToastBanner(UiLocalizer.Pick(
                        "✓ Document Quality Scorecard refreshed.",
                        "✓ စာတမ်းအရည်အသွေး ရမှတ်ကို အသစ်ပြန်လည် တွက်ချက်ပြီးပါပြီ။"));
                }
            };

            var btnExportCert = CreatePillButton(
                UiLocalizer.Pick("✦ Export Audit Report (Word)", "✦ အစီရင်ခံစာ ထုတ်မည် (Word)"),
                Color.FromArgb(22, 78, 166), Color.White, Color.FromArgb(18, 66, 145), _fontSmallBold);
            btnExportCert.Click += (s, e) => RequestExportAuditCertificate?.Invoke();

            qBtnGrid.Controls.Add(btnRefreshScore, 0, 0);
            qBtnGrid.Controls.Add(btnExportCert, 1, 0);
            qualityCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            qualityCard.Controls.Add(qBtnGrid, 0, qRow++);
            _cardsPanel.Controls.Add(qualityCard);

            // ------------------------------------------------------------------
            // CARD 2: One-Click Legacy Font, Numeral & Punctuation Toolkit
            // ------------------------------------------------------------------
            var fontCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188), isSelectableItem: null);
            int fRow = 0;

            var lblFHeader = new Label
            {
                Text = UiLocalizer.Pick(
                    "2. Font Converter, Numerals & Punctuation Cleaner",
                    "၂။ ဖောင့်ပြောင်းစက်၊ ကိန်းဂဏန်း နှင့် ပုဒ်ဖြတ် သန့်စင်ရေး"),
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(18, 48, 104),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 3),
                Padding = new Padding(2, 1, 2, 1)
            };
            fontCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fontCard.Controls.Add(lblFHeader, 0, fRow++);

            var lblFHint = new Label
            {
                Text = UiLocalizer.Pick(
                    "Converts in one click while preserving English fonts (Calibri/Arial), Bold/Italic, and tables (Ctrl+Z to undo).",
                    "အင်္ဂလိပ်ဖောင့် (Calibri/Arial)၊ စာလုံးအထူ/အစောင်းနှင့် ဇယားကွက်များ မပျက်စီးဘဲ တစ်ချက်နှိပ် ပြောင်းလဲပေးသည် (Ctrl+Z ပြန်ဆုတ်နိုင်သည်)။"),
                Font = _fontSmall,
                ForeColor = Color.FromArgb(68, 78, 94),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(2, 1, 2, 1)
            };
            fontCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fontCard.Controls.Add(lblFHint, 0, fRow++);

            var chkSelectionOnly = new CheckBox
            {
                Text = UiLocalizer.Pick(
                    "Apply to Selected Text Only",
                    "ရွေးချယ်ထားသော စာသားသာ ပြောင်းမည် (Selection Only)"),
                Checked = _toolkitSelectionOnly,
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(35, 58, 96),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 2, 6)
            };
            chkSelectionOnly.CheckedChanged += (s, e) => _toolkitSelectionOnly = chkSelectionOnly.Checked;
            fontCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fontCard.Controls.Add(chkSelectionOnly, 0, fRow++);

            var btnConvertFonts = CreatePillButton(
                UiLocalizer.Pick(
                    "⇄ Convert WinInnwa / Zawgyi ➔ Pyidaungsu",
                    "⇄ WinInnwa / Zawgyi ➔ Pyidaungsu ပြောင်းမည်"),
                Color.FromArgb(20, 82, 174),
                Color.White,
                Color.FromArgb(16, 68, 150),
                _fontSmallBold);
            btnConvertFonts.Margin = new Padding(2, 0, 2, 5);
            btnConvertFonts.Click += (s, e) => RequestConvertLegacyFonts?.Invoke(_toolkitSelectionOnly);
            fontCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fontCard.Controls.Add(btnConvertFonts, 0, fRow++);

            var numFmtGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 5)
            };
            numFmtGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54F));
            numFmtGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
            numFmtGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnToMmDigits = CreatePillButton("0-9 ➔ ၀-၉ (Smart)", Color.FromArgb(236, 244, 255), Color.FromArgb(18, 68, 150), Color.FromArgb(190, 212, 246), _fontSmallBold);
            btnToMmDigits.Click += (s, e) => RequestFormatNumerals?.Invoke(true, _toolkitSelectionOnly);
            _toolTip.SetToolTip(
                btnToMmDigits,
                UiLocalizer.Pick(
                    "Converts numerals to Myanmar digits while preserving URLs, emails, COVID-19, ISO-9001, A4 & 5G codes",
                    "URL, Email, COVID-19, ISO-9001, A4, 5G တို့ကို မထိခိုက်ဘဲ မြန်မာဂဏန်း ပြောင်းပေးသည်"));

            var btnToAsciiDigits = CreatePillButton("၀-၉ ➔ 0-9", Color.FromArgb(242, 245, 250), Color.FromArgb(48, 58, 74), Color.FromArgb(214, 222, 234), _fontSmallBold);
            btnToAsciiDigits.Click += (s, e) => RequestFormatNumerals?.Invoke(false, _toolkitSelectionOnly);

            numFmtGrid.Controls.Add(btnToMmDigits, 0, 0);
            numFmtGrid.Controls.Add(btnToAsciiDigits, 1, 0);
            fontCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fontCard.Controls.Add(numFmtGrid, 0, fRow++);

            var btnCleanPunct = CreatePillButton(
                UiLocalizer.Pick(
                    "✨ Clean Punctuation (၊ / ။) Spacing & ZWSP",
                    "✨ ပုဒ်ဖြတ် (၊ / ။) အကွာအဝေး နှင့် ZWSP သန့်စင်မည်"),
                Color.FromArgb(235, 248, 240),
                Color.FromArgb(18, 108, 52),
                Color.FromArgb(182, 226, 198),
                _fontSmallBold);
            btnCleanPunct.Click += (s, e) => RequestFormatPunctuation?.Invoke(_toolkitSelectionOnly);
            fontCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fontCard.Controls.Add(btnCleanPunct, 0, fRow++);

            _cardsPanel.Controls.Add(fontCard);

            // ------------------------------------------------------------------
            // CARD 2B: One-Click Colloquial -> Official Ministry Prose Rewriter
            // ------------------------------------------------------------------
            var rewriteCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(92, 52, 168), isSelectableItem: null);
            int rwRow = 0;

            var lblRwHeader = new Label
            {
                Text = UiLocalizer.Pick(
                    "2B. Colloquial ➔ Official / Literary Style Rewriter",
                    "၂(ခ)။ စကားပြောဟန် ➔ ရုံးသုံး/စာပေဟန် အလိုအလျောက်ပြောင်းစက်"),
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(64, 34, 128),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 3),
                Padding = new Padding(2, 1, 2, 1)
            };
            rewriteCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rewriteCard.Controls.Add(lblRwHeader, 0, rwRow++);

            var lblRwHint = new Label
            {
                Text = UiLocalizer.Pick(
                    "Converts spoken particles (ပါတယ်, တွေ, နဲ့, ဒီနေ့, မရှိဘူး, ဆိုရင်, လို့...) into formal official/literary Burmese without corrupting lexical roots like ဟိုတယ် or ချို့တဲ့.",
                    "နေ့စဉ် အပြောစကား (ပါတယ်၊ တွေ၊ နဲ့၊ ဒီနေ့၊ မရှိဘူး၊ ဆိုရင်၊ လို့...) များကို အစိုးရရုံးသုံး စာပေအရေးအသားသို့ တစ်ချက်တည်းဖြင့် ပြောင်းလဲပေးသည် (ဟိုတယ်၊ ချို့တဲ့ စသည့် မူရင်းဝေါဟာရများကို မထိခိုက်ပါ)။"),
                Font = _fontSmall,
                ForeColor = Color.FromArgb(68, 78, 94),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(2, 1, 2, 1)
            };
            rewriteCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rewriteCard.Controls.Add(lblRwHint, 0, rwRow++);

            var txtRwInput = new TextBox
            {
                Text = _rewriterInputSample,
                Font = _fontSmall,
                Multiline = true,
                Height = 56,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 2, 2, 5)
            };
            rewriteCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rewriteCard.Controls.Add(txtRwInput, 0, rwRow++);

            OfficialRewriteResult initialRw = MyanmarOfficialRewriter.ConvertToOfficialProse(_rewriterInputSample);
            var lblRwPreview = new Label
            {
                Text = UiLocalizer.Pick(
                    $"Official Style ({initialRw.ReplacementCount} replacement(s)):\n{initialRw.RewrittenText}",
                    $"ရုံးသုံးဟန် ({initialRw.ReplacementCount} နေရာ ပြောင်းလဲပြီး):\n{initialRw.RewrittenText}"),
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(54, 28, 114),
                BackColor = Color.FromArgb(245, 241, 255),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 6, 8, 6)
            };
            txtRwInput.TextChanged += (s, e) =>
            {
                _rewriterInputSample = txtRwInput.Text;
                OfficialRewriteResult rw = MyanmarOfficialRewriter.ConvertToOfficialProse(_rewriterInputSample);
                lblRwPreview.Text = UiLocalizer.Pick(
                    $"Official Style ({rw.ReplacementCount} replacement(s)):\n{rw.RewrittenText}",
                    $"ရုံးသုံးဟန် ({rw.ReplacementCount} နေရာ ပြောင်းလဲပြီး):\n{rw.RewrittenText}");
            };
            rewriteCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rewriteCard.Controls.Add(lblRwPreview, 0, rwRow++);

            var rwBtnGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            rwBtnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            rwBtnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            rwBtnGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnCopyRewritten = CreatePillButton(
                UiLocalizer.Pick("Copy Preview", "ကူးယူမည်"),
                Color.FromArgb(243, 240, 252),
                Color.FromArgb(62, 34, 124),
                Color.FromArgb(208, 198, 242),
                _fontSmall);
            btnCopyRewritten.Click += (s, e) =>
            {
                try
                {
                    OfficialRewriteResult rw = MyanmarOfficialRewriter.ConvertToOfficialProse(txtRwInput.Text);
                    if (!string.IsNullOrEmpty(rw.RewrittenText))
                    {
                        Clipboard.SetText(rw.RewrittenText);
                        ShowToastBanner(UiLocalizer.Pick(
                            "✓ Official/literary prose copied to clipboard.",
                            "✓ ရုံးသုံး/စာပေဟန် စာသားကို ကူးယူပြီးပါပြီ။"));
                    }
                }
                catch { }
            };

            var btnRewriteDoc = CreatePillButton(
                UiLocalizer.Pick("✦ Rewrite in Document", "✦ စာတမ်း၌ ရုံးသုံးဟန် ပြောင်းမည်"),
                Color.FromArgb(92, 52, 168),
                Color.White,
                Color.FromArgb(74, 40, 142),
                _fontSmallBold);
            _toolTip.SetToolTip(
                btnRewriteDoc,
                UiLocalizer.Pick(
                    "Convert conversational expressions across the document or selection into formal official prose (Single Ctrl+Z)",
                    "စာတမ်းတစ်ခုလုံး သို့မဟုတ် ရွေးချယ်ထားသော စာသားရှိ အပြောစကားများကို ရုံးသုံးစာပေဟန်သို့ ပြောင်းလဲရန် (Single Ctrl+Z)"));
            btnRewriteDoc.Click += (s, e) => RequestRewriteToOfficialStyle?.Invoke(_toolkitSelectionOnly);

            rwBtnGrid.Controls.Add(btnCopyRewritten, 0, 0);
            rwBtnGrid.Controls.Add(btnRewriteDoc, 1, 0);
            rewriteCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rewriteCard.Controls.Add(rwBtnGrid, 0, rwRow++);

            _cardsPanel.Controls.Add(rewriteCard);

            // ------------------------------------------------------------------
            // CARD 3: Banking Kyat (သိန်း) & Cardinal Number-to-Words Converter
            // ------------------------------------------------------------------
            var numCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(16, 122, 108), isSelectableItem: null);
            int nRow = 0;

            var lblNHeader = new Label
            {
                Text = UiLocalizer.Pick(
                    "3. Kyat (သိန်း) & Number-to-Words Converter",
                    "၃။ ကျပ်ငွေ (သိန်း) နှင့် ကိန်းဂဏန်း စာသားပြောင်းစက်"),
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(14, 86, 78),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(2, 1, 2, 1)
            };
            numCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            numCard.Controls.Add(lblNHeader, 0, nRow++);

            var modeGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 5)
            };
            modeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            modeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            modeGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            bool isBanking = _numberMode == NumberConversionMode.BankingKyatLakh;
            var btnModeBank = CreatePillButton(
                UiLocalizer.Pick("Banking / Kyat (သိန်း)", "ဘဏ်/ငွေစာရင်း (သိန်း)"),
                isBanking ? Color.FromArgb(16, 122, 108) : Color.FromArgb(240, 244, 249),
                isBanking ? Color.White : Color.FromArgb(48, 58, 74),
                isBanking ? Color.FromArgb(16, 122, 108) : Color.FromArgb(214, 222, 232),
                isBanking ? _fontSmallBold : _fontSmall);

            var btnModeCard = CreatePillButton(
                UiLocalizer.Pick("General (သန်း)", "အထွေထွေ (သန်း)"),
                !isBanking ? Color.FromArgb(16, 122, 108) : Color.FromArgb(240, 244, 249),
                !isBanking ? Color.White : Color.FromArgb(48, 58, 74),
                !isBanking ? Color.FromArgb(16, 122, 108) : Color.FromArgb(214, 222, 232),
                !isBanking ? _fontSmallBold : _fontSmall);

            var txtNumInput = new TextBox
            {
                Text = _numberInputSample,
                Font = _fontInput,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 2, 2, 6)
            };

            NumberConversionResult initialConv = MyanmarNumberToWordsConverter.Convert(_numberInputSample, _numberMode);
            var lblNumPreview = new Label
            {
                Text = initialConv.Success
                    ? UiLocalizer.Pick(
                        $"Words: {initialConv.BurmeseWords}\nVoucher: {initialConv.VoucherInlineText}",
                        $"စာသား: {initialConv.BurmeseWords}\nဘောင်ချာ: {initialConv.VoucherInlineText}")
                    : initialConv.ErrorMessage,
                Font = _fontSmallBold,
                ForeColor = initialConv.Success ? Color.FromArgb(14, 88, 78) : Color.FromArgb(172, 68, 16),
                BackColor = Color.FromArgb(238, 250, 247),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 6, 8, 6)
            };

            Action refreshNumberPreview = () =>
            {
                _numberInputSample = txtNumInput.Text;
                NumberConversionResult r = MyanmarNumberToWordsConverter.Convert(_numberInputSample, _numberMode);
                lblNumPreview.Text = r.Success
                    ? (_numberMode == NumberConversionMode.BankingKyatLakh
                        ? UiLocalizer.Pick(
                            $"Words: {r.BurmeseWords}\nVoucher: {r.VoucherInlineText}",
                            $"စာသား: {r.BurmeseWords}\nဘောင်ချာ: {r.VoucherInlineText}")
                        : UiLocalizer.Pick(
                            $"Cardinal Words: {r.BurmeseWords}\nMyanmar Digits: {r.NormalizedMyanmarDigits}",
                            $"ရိုးရိုးကိန်းဂဏန်း: {r.BurmeseWords}\nမြန်မာဂဏန်း: {r.NormalizedMyanmarDigits}"))
                    : r.ErrorMessage;
                lblNumPreview.ForeColor = r.Success ? Color.FromArgb(14, 88, 78) : Color.FromArgb(172, 68, 16);
            };

            btnModeBank.Click += (s, e) =>
            {
                _numberMode = NumberConversionMode.BankingKyatLakh;
                RenderToolkitWorkspaceView();
            };
            btnModeCard.Click += (s, e) =>
            {
                _numberMode = NumberConversionMode.GeneralCardinal;
                RenderToolkitWorkspaceView();
            };
            txtNumInput.TextChanged += (s, e) => refreshNumberPreview();

            modeGrid.Controls.Add(btnModeBank, 0, 0);
            modeGrid.Controls.Add(btnModeCard, 1, 0);
            numCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            numCard.Controls.Add(modeGrid, 0, nRow++);

            numCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            numCard.Controls.Add(txtNumInput, 0, nRow++);

            numCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            numCard.Controls.Add(lblNumPreview, 0, nRow++);

            var numBtnGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            numBtnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            numBtnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            numBtnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            numBtnGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnCopyNum = CreatePillButton(
                UiLocalizer.Pick("Copy", "ကူးယူ"),
                Color.FromArgb(242, 245, 250), Color.FromArgb(48, 58, 74), Color.FromArgb(215, 222, 232), _fontSmall);
            btnCopyNum.Click += (s, e) =>
            {
                var r = MyanmarNumberToWordsConverter.Convert(_numberInputSample, _numberMode);
                if (r.Success)
                {
                    try
                    {
                        Clipboard.SetText(r.BurmeseWords);
                        ShowToastBanner(UiLocalizer.Pick(
                            "✓ Myanmar number words copied to clipboard.",
                            "✓ ကျပ်ငွေ/ကိန်းဂဏန်း စာသားကို ကူးယူပြီးပါပြီ။"));
                    }
                    catch { }
                }
            };

            var btnInsWords = CreatePillButton(
                UiLocalizer.Pick("Insert Words ➔", "စာသား ထည့် ➔"),
                Color.FromArgb(234, 248, 245), Color.FromArgb(14, 96, 86), Color.FromArgb(178, 224, 214), _fontSmallBold);
            btnInsWords.Click += (s, e) =>
            {
                var r = MyanmarNumberToWordsConverter.Convert(_numberInputSample, _numberMode);
                if (r.Success) RequestInsertWordAtCursor?.Invoke(r.BurmeseWords);
            };

            var btnInsVoucher = CreatePillButton(
                UiLocalizer.Pick("Voucher ➔", "ဘောင်ချာ ➔"),
                Color.FromArgb(16, 122, 108), Color.White, Color.FromArgb(12, 98, 86), _fontSmallBold);
            btnInsVoucher.Click += (s, e) =>
            {
                var r = MyanmarNumberToWordsConverter.Convert(_numberInputSample, NumberConversionMode.BankingKyatLakh);
                if (r.Success) RequestInsertWordAtCursor?.Invoke(r.VoucherInlineText);
            };

            numBtnGrid.Controls.Add(btnCopyNum, 0, 0);
            numBtnGrid.Controls.Add(btnInsWords, 1, 0);
            numBtnGrid.Controls.Add(btnInsVoucher, 2, 0);
            numCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            numCard.Controls.Add(numBtnGrid, 0, nRow++);

            _cardsPanel.Controls.Add(numCard);

            // ------------------------------------------------------------------
            // CARD 4: Offline မြန်မာသက္ကရာဇ် Calendar & Ministry ရုံးစာ Suite
            // ------------------------------------------------------------------
            var offCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(172, 96, 14), isSelectableItem: null);
            int oRow = 0;

            var lblOHeader = new Label
            {
                Text = UiLocalizer.Pick(
                    "4. Myanmar Calendar & Official Letter Templates",
                    "၄။ မြန်မာသက္ကရာဇ် ပြက္ခဒိန် နှင့် ရုံးစာ ပုံစံခွက်များ"),
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(128, 68, 8),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(2, 1, 2, 1)
            };
            offCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            offCard.Controls.Add(lblOHeader, 0, oRow++);

            var dtPicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Value = _selectedCalendarDate,
                Font = _fontBody,
                Dock = DockStyle.Top,
                Margin = new Padding(2, 0, 2, 6)
            };

            Func<DateTime, string> formatCalendarSummary = dt =>
            {
                MyanmarDateInfo info = MyanmarTraditionalCalendar.FromGregorian(dt);
                string extra = !string.IsNullOrEmpty(info.SabbathLabel)
                    ? $" • {info.SabbathLabel}"
                    : (!string.IsNullOrEmpty(info.AstrologicalNote) ? $" • {info.AstrologicalNote}" : string.Empty);
                return UiLocalizer.Pick(
                    $"Myanmar Date: {info.OfficialMyanmarDateText} ({info.WeekdayBurmese}နေ့)\n" +
                    $"Sasana Year: {MyanmarTraditionalCalendar.ToMyanmarDigits(info.SasanaYear)} • {info.YearTypeLabel}{extra}",
                    $"ရုံးစာရက်စွဲ: {info.OfficialMyanmarDateText} ({info.WeekdayBurmese}နေ့)\n" +
                    $"သာသနာနှစ်: {MyanmarTraditionalCalendar.ToMyanmarDigits(info.SasanaYear)} ခုနှစ် • {info.YearTypeLabel}{extra}");
            };

            var lblCalPreview = new Label
            {
                Text = formatCalendarSummary(_selectedCalendarDate),
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(122, 64, 8),
                BackColor = Color.FromArgb(255, 249, 236),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 6, 8, 6)
            };

            dtPicker.ValueChanged += (s, e) =>
            {
                _selectedCalendarDate = dtPicker.Value.Date;
                lblCalPreview.Text = formatCalendarSummary(_selectedCalendarDate);
            };

            offCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            offCard.Controls.Add(dtPicker, 0, oRow++);
            offCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            offCard.Controls.Add(lblCalPreview, 0, oRow++);

            var dateInsGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 6)
            };
            dateInsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            dateInsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            dateInsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnInsMmDate = CreatePillButton(
                UiLocalizer.Pick("Myanmar Date ➔", "မြန်မာရက်စွဲ ထည့် ➔"),
                Color.FromArgb(254, 245, 228), Color.FromArgb(138, 72, 8), Color.FromArgb(238, 208, 162), _fontSmallBold);
            btnInsMmDate.Click += (s, e) =>
            {
                var info = MyanmarTraditionalCalendar.FromGregorian(_selectedCalendarDate);
                RequestInsertWordAtCursor?.Invoke(info.OfficialMyanmarDateText);
            };

            var btnInsDualDate = CreatePillButton(
                UiLocalizer.Pick("Dual Official Date ➔", "နှစ်မျိုးတွဲ ရက်စွဲ ➔"),
                Color.FromArgb(182, 102, 14), Color.White, Color.FromArgb(156, 86, 10), _fontSmallBold);
            btnInsDualDate.Click += (s, e) =>
            {
                var info = MyanmarTraditionalCalendar.FromGregorian(_selectedCalendarDate);
                RequestInsertWordAtCursor?.Invoke(info.DualOfficialHeaderText);
            };

            dateInsGrid.Controls.Add(btnInsMmDate, 0, 0);
            dateInsGrid.Controls.Add(btnInsDualDate, 1, 0);
            offCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            offCard.Controls.Add(dateInsGrid, 0, oRow++);

            var lblTplSub = new Label
            {
                Text = UiLocalizer.Pick(
                    "Official Burmese Templates (inserted in Pyidaungsu font) —",
                    "ရုံးသုံး စံပုံစံခွက် (Pyidaungsu ဖောင့်ဖြင့် အသင့်ထည့်သွင်းရန်) —"),
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(72, 82, 96),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 4),
                Padding = new Padding(2, 1, 2, 1)
            };
            offCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            offCard.Controls.Add(lblTplSub, 0, oRow++);

            var tplGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 6)
            };
            tplGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tplGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tplGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tplGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnTplLetter = CreatePillButton(
                UiLocalizer.Pick("✦ Official Letter", "✦ ရုံးစာ ပုံစံကြမ်း"),
                Color.FromArgb(240, 245, 255), Color.FromArgb(20, 68, 148), Color.FromArgb(196, 214, 245), _fontSmallBold);
            btnTplLetter.Click += (s, e) => RequestInsertOfficialTemplate?.Invoke(OfficialTemplateKind.MinistryOfficialLetter, _selectedCalendarDate);

            var btnTplGazette = CreatePillButton(
                UiLocalizer.Pick("✦ Gazette Notice", "✦ အမိန့်ကြော်ငြာစာ"),
                Color.FromArgb(240, 245, 255), Color.FromArgb(20, 68, 148), Color.FromArgb(196, 214, 245), _fontSmallBold);
            btnTplGazette.Click += (s, e) => RequestInsertOfficialTemplate?.Invoke(OfficialTemplateKind.GazetteNotification, _selectedCalendarDate);

            var btnTplMinutes = CreatePillButton(
                UiLocalizer.Pick("✦ Meeting Minutes", "✦ အစည်းအဝေးမှတ်တမ်း"),
                Color.FromArgb(240, 245, 255), Color.FromArgb(20, 68, 148), Color.FromArgb(196, 214, 245), _fontSmallBold);
            btnTplMinutes.Click += (s, e) => RequestInsertOfficialTemplate?.Invoke(OfficialTemplateKind.MeetingMinutes, _selectedCalendarDate);

            var btnValidateLetter = CreatePillButton(
                UiLocalizer.Pick("✓ 7-Point Letter Check", "✓ ရုံးစာ (၇) ချက် စစ်မည်"),
                Color.FromArgb(232, 246, 238), Color.FromArgb(18, 108, 52), Color.FromArgb(178, 224, 194), _fontSmallBold);
            btnValidateLetter.Click += (s, e) =>
            {
                if (RequestValidateOfficialLetter != null)
                {
                    _latestOfficialAudit = RequestValidateOfficialLetter.Invoke();
                    RenderToolkitWorkspaceView();
                }
            };

            tplGrid.Controls.Add(btnTplLetter, 0, 0);
            tplGrid.Controls.Add(btnTplGazette, 1, 0);
            tplGrid.Controls.Add(btnTplMinutes, 0, 1);
            tplGrid.Controls.Add(btnValidateLetter, 1, 1);
            offCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            offCard.Controls.Add(tplGrid, 0, oRow++);

            if (_latestOfficialAudit != null && _latestOfficialAudit.Items.Count > 0)
            {
                var sbAudit = new System.Text.StringBuilder();
                sbAudit.AppendLine(UiLocalizer.Pick(
                    $"Official Letter Structure Audit ({_latestOfficialAudit.SummaryStatus}) —",
                    $"ရုံးစာ ဖွဲ့စည်းပုံ စစ်ဆေးချက် ({_latestOfficialAudit.SummaryStatus}) —"));
                foreach (var chk in _latestOfficialAudit.Items)
                {
                    string mark = chk.Passed ? "✓" : (chk.IsAdvisory ? "ℹ" : "✗");
                    sbAudit.AppendLine($"{mark} {chk.CriterionName}: {chk.Detail}");
                }

                var lblAuditChecklist = new Label
                {
                    Text = sbAudit.ToString().TrimEnd(),
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(38, 52, 72),
                    BackColor = Color.FromArgb(245, 248, 253),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    Padding = new Padding(8, 6, 8, 6)
                };
                offCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                offCard.Controls.Add(lblAuditChecklist, 0, oRow++);
            }

            _cardsPanel.Controls.Add(offCard);
            _cardsPanel.ResumeLayout();
        }
    }
}
