using System;
using System.Drawing;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Official;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    public sealed partial class ExcelProofingTaskPaneControl
    {
        private void RenderToolkitWorkspace()
        {
            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            if (_latestQualityReport == null && RequestRefreshQualityReport != null)
            {
                try { _latestQualityReport = RequestRefreshQualityReport.Invoke(); } catch { }
            }

            // 1. Quality Scorecard Card
            _cardsPanel.Controls.Add(BuildQualityScoreCard(cardWidth, innerWrap));

            // 2. Formula-Safe Batch Cell Converter Card
            _cardsPanel.Controls.Add(BuildBatchCellToolkitCard(cardWidth, innerWrap));

            // 3. Kyat Lakh Accounting Converter Card (Excel Budget / Voucher Favorite!)
            _cardsPanel.Controls.Add(BuildKyatLakhConverterCard(cardWidth, innerWrap));

            // 4. Myanmar Traditional Calendar Card
            _cardsPanel.Controls.Add(BuildMyanmarCalendarCard(cardWidth, innerWrap));

            _cardsPanel.ResumeLayout(true);
        }

        private Control BuildQualityScoreCard(int cardWidth, int innerWrap)
        {
            DocumentQualityReport rep = _latestQualityReport ?? new DocumentQualityReport();
            Color stripe = rep.OverallScore >= 90
                ? Color.FromArgb(16, 124, 65)
                : (rep.OverallScore >= 75 ? Color.FromArgb(24, 90, 188) : Color.FromArgb(217, 138, 18));

            var card = CreateStyledCardContainer(cardWidth, stripe);
            int r = 0;

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label
            {
                Text = $"📊 Excel မြန်မာစာ အရည်အသွေး — {rep.OverallScore}% ({rep.GradeLabel})",
                Font = _fontBodyBold,
                ForeColor = stripe,
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Margin = new Padding(0, 0, 0, 4)
            }, 0, r++);

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label
            {
                Text = $"• Unicode စံနှုန်းသန့်စင်မှု: {rep.UnicodePurityPercent:0.#}%   |   ရုံးသုံးဟန်: {rep.FormalRegisterPercent:0.#}%\n• ဝဏ္ဏရေ: {rep.MyanmarSyllableCount:N0}   |   သတ်ပုံ: {rep.SpellingIssueCount}   |   သဒ္ဒါ: {rep.GrammarIssueCount}   |   အနက်သံသယ: {rep.ContextWarningCount}",
                Font = _fontSmall,
                ForeColor = Color.FromArgb(56, 68, 84),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Margin = new Padding(0, 0, 0, 6)
            }, 0, r++);

            var btnRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1
            };
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
            btnRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnRefresh = CreateActionButton("↻ အမှတ်ပြန်တွက်", Color.FromArgb(242, 245, 250), Color.FromArgb(36, 52, 76), Color.FromArgb(210, 218, 232), _fontBrandStats);
            btnRefresh.Margin = new Padding(0, 0, 4, 0);
            btnRefresh.Click += (s, e) =>
            {
                _latestQualityReport = RequestRefreshQualityReport?.Invoke();
                RenderToolkitWorkspace();
            };

            var btnExport = CreateActionButton("📄 အစီရင်ခံစာ Excel ထုတ်မည်", Color.FromArgb(16, 124, 65), Color.White, Color.FromArgb(14, 102, 53), _fontBrandStats);
            btnExport.Click += (s, e) => RequestExportAuditWorkbook?.Invoke();

            btnRow.Controls.Add(btnRefresh, 0, 0);
            btnRow.Controls.Add(btnExport, 1, 0);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnRow, 0, r++);

            return card;
        }

        private Control BuildBatchCellToolkitCard(int cardWidth, int innerWrap)
        {
            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188));
            int r = 0;

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label
            {
                Text = "🛠 ဇယားကွက် စာသားပြောင်းလဲရေး ကိရိယာများ (Formula-Safe + Undo)",
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(18, 48, 104),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Margin = new Padding(0, 0, 0, 5)
            }, 0, r++);

            var scopeToggle = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(238, 242, 249),
                Padding = new Padding(2),
                Margin = new Padding(0, 0, 0, 6)
            };
            scopeToggle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            scopeToggle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            scopeToggle.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnFullSheet = CreateTabSegmentButton("လက်ရှိ Sheet အားလုံး");
            var btnSelOnly = CreateTabSegmentButton("ရွေးထားကွက်သာ (Selection)");
            StyleSegment(btnFullSheet, !_toolkitSelectionOnly, Color.FromArgb(24, 90, 188));
            StyleSegment(btnSelOnly, _toolkitSelectionOnly, Color.FromArgb(24, 90, 188));

            btnFullSheet.Click += (s, e) =>
            {
                _toolkitSelectionOnly = false;
                RenderToolkitWorkspace();
            };
            btnSelOnly.Click += (s, e) =>
            {
                _toolkitSelectionOnly = true;
                RenderToolkitWorkspace();
            };
            scopeToggle.Controls.Add(btnFullSheet, 0, 0);
            scopeToggle.Controls.Add(btnSelOnly, 1, 0);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(scopeToggle, 0, r++);

            var btnFont = CreateActionButton(
                "WinInnwa / Zawgyi  ➔  Pyidaungsu Unicode",
                Color.FromArgb(238, 244, 255),
                Color.FromArgb(18, 68, 148),
                Color.FromArgb(188, 210, 245),
                _fontSmallBold);
            btnFont.Margin = new Padding(0, 0, 0, 5);
            btnFont.Click += (s, e) => RequestConvertLegacyFonts?.Invoke(_toolkitSelectionOnly);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnFont, 0, r++);

            var numGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 5)
            };
            numGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            numGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            numGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnToMm = CreateActionButton("0-9 ➔ ၀-၉ (စာသားကွက်)", Color.FromArgb(244, 247, 252), Color.FromArgb(34, 48, 68), Color.FromArgb(210, 218, 232), _fontBrandStats);
            btnToMm.Margin = new Padding(0, 0, 3, 0);
            btnToMm.Click += (s, e) => RequestFormatNumerals?.Invoke(true, _toolkitSelectionOnly);

            var btnToAscii = CreateActionButton("၀-၉ ➔ 0-9 (စာသားကွက်)", Color.FromArgb(244, 247, 252), Color.FromArgb(34, 48, 68), Color.FromArgb(210, 218, 232), _fontBrandStats);
            btnToAscii.Margin = new Padding(2, 0, 0, 0);
            btnToAscii.Click += (s, e) => RequestFormatNumerals?.Invoke(false, _toolkitSelectionOnly);

            numGrid.Controls.Add(btnToMm, 0, 0);
            numGrid.Controls.Add(btnToAscii, 1, 0);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(numGrid, 0, r++);

            var btnRewrite = CreateActionButton(
                "စကားပြောဟန် (ပါတယ်/တွေ)  ➔  ရုံးသုံးစာပေဟန်",
                Color.FromArgb(234, 246, 239),
                Color.FromArgb(16, 98, 52),
                Color.FromArgb(182, 222, 196),
                _fontSmallBold);
            btnRewrite.Margin = new Padding(0, 0, 0, 5);
            btnRewrite.Click += (s, e) => RequestRewriteToOfficialStyle?.Invoke(_toolkitSelectionOnly);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnRewrite, 0, r++);

            var btnPunct = CreateActionButton(
                "ပုဒ်ဖြတ်ပုဒ်ရပ် (၊ / ။) နှင့် မမြင်ရသော ZWSP သန့်စင်မည်",
                Color.FromArgb(246, 248, 252),
                Color.FromArgb(46, 58, 76),
                Color.FromArgb(212, 220, 232),
                _fontBrandStats);
            btnPunct.Click += (s, e) => RequestFormatPunctuation?.Invoke(_toolkitSelectionOnly);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnPunct, 0, r++);

            return card;
        }

        private Control BuildKyatLakhConverterCard(int cardWidth, int innerWrap)
        {
            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(16, 124, 65));
            int r = 0;

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label
            {
                Text = "💰 ကျပ်ငွေ (သိန်း/သန်း) ဘဏ္ဍာရေး စာသားပြောင်းစက်",
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(16, 96, 50),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Margin = new Padding(0, 0, 0, 5)
            }, 0, r++);

            var inputRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 5)
            };
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            inputRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var txtNum = new TextBox
            {
                Text = _numberInputSample,
                Font = _fontInput,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 4, 2)
            };

            var lblResult = new Label
            {
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(16, 98, 52),
                BackColor = Color.FromArgb(236, 248, 240),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 6, 8, 6),
                Margin = new Padding(0, 0, 0, 5)
            };

            void RefreshKyatOutput()
            {
                _numberInputSample = txtNum.Text;
                NumberConversionResult conv = MyanmarNumberToWordsConverter.Convert(_numberInputSample, _numberMode);
                lblResult.Text = conv.Success ? conv.BurmeseWords : conv.ErrorMessage;
                lblResult.ForeColor = conv.Success ? Color.FromArgb(16, 98, 52) : Color.FromArgb(168, 36, 36);
            }

            txtNum.TextChanged += (s, e) => RefreshKyatOutput();

            var btnReadCell = CreateActionButton("⚡ ရွေးကွက်မှဖတ်", Color.FromArgb(238, 245, 255), Color.FromArgb(20, 74, 158), Color.FromArgb(192, 212, 245), _fontBrandStats);
            btnReadCell.Click += (s, e) =>
            {
                string cellNum = RequestGetSelectedNumericText?.Invoke() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(cellNum))
                {
                    txtNum.Text = cellNum;
                }
                else
                {
                    ShowToastBanner("⚠ ကျေးဇူးပြု၍ Excel ထဲမှ ကိန်းဂဏန်းပါသော အကွက်ကို အရင်နှိပ်ရွေးပါ။", isSuccess: false);
                }
            };

            inputRow.Controls.Add(txtNum, 0, 0);
            inputRow.Controls.Add(btnReadCell, 1, 0);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(inputRow, 0, r++);

            var modeFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 4)
            };
            var btnLakh = CreatePillButton(
                "ကျပ်သိန်းစနစ်",
                _numberMode == NumberConversionMode.BankingKyatLakh ? Color.FromArgb(16, 124, 65) : Color.FromArgb(242, 245, 250),
                _numberMode == NumberConversionMode.BankingKyatLakh ? Color.White : Color.FromArgb(52, 64, 82),
                Color.FromArgb(198, 212, 228),
                _fontBrandStats);
            var btnCard = CreatePillButton(
                "သန်းစနစ် (ရိုးရိုး)",
                _numberMode == NumberConversionMode.GeneralCardinal ? Color.FromArgb(16, 124, 65) : Color.FromArgb(242, 245, 250),
                _numberMode == NumberConversionMode.GeneralCardinal ? Color.White : Color.FromArgb(52, 64, 82),
                Color.FromArgb(198, 212, 228),
                _fontBrandStats);

            btnLakh.Click += (s, e) => { _numberMode = NumberConversionMode.BankingKyatLakh; RenderToolkitWorkspace(); };
            btnCard.Click += (s, e) => { _numberMode = NumberConversionMode.GeneralCardinal; RenderToolkitWorkspace(); };
            modeFlow.Controls.Add(btnLakh);
            modeFlow.Controls.Add(btnCard);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(modeFlow, 0, r++);

            RefreshKyatOutput();
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblResult, 0, r++);

            var actFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            var btnCopyWords = CreatePillButton("စာသား ကူးယူ (Copy)", Color.FromArgb(242, 245, 250), Color.FromArgb(44, 56, 74), Color.FromArgb(212, 220, 232), _fontBrandStats);
            btnCopyWords.Click += (s, e) =>
            {
                var conv = MyanmarNumberToWordsConverter.Convert(txtNum.Text, _numberMode);
                if (conv.Success)
                {
                    try { Clipboard.SetText(conv.BurmeseWords); ShowToastBanner("✓ ငွေပမာဏ စာသားကို ကူးယူပြီးပါပြီ။"); } catch { }
                }
            };
            var btnInsertWords = CreatePillButton("+ လက်ရှိအကွက်ထဲထည့်", Color.FromArgb(232, 246, 237), Color.FromArgb(16, 102, 52), Color.FromArgb(182, 222, 196), _fontBrandStats);
            btnInsertWords.Click += (s, e) =>
            {
                var conv = MyanmarNumberToWordsConverter.Convert(txtNum.Text, _numberMode);
                if (conv.Success)
                {
                    RequestInsertTextIntoCell?.Invoke(conv.BurmeseWords);
                }
            };
            actFlow.Controls.Add(btnCopyWords);
            actFlow.Controls.Add(btnInsertWords);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(actFlow, 0, r++);

            return card;
        }

        private Control BuildMyanmarCalendarCard(int cardWidth, int innerWrap)
        {
            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(196, 118, 12));
            int r = 0;

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label
            {
                Text = "📅 မြန်မာသက္ကရာဇ် နှင့် ရုံးစာ ရက်စွဲပြက္ခဒိန် (Offline)",
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(148, 86, 8),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Margin = new Padding(0, 0, 0, 5)
            }, 0, r++);

            var dtPicker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Long,
                Value = _selectedCalendarDate,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 5)
            };

            var lblCalOut = new Label
            {
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(44, 54, 70),
                BackColor = Color.FromArgb(255, 249, 235),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 6, 8, 6),
                Margin = new Padding(0, 0, 0, 5)
            };

            void UpdateDateText()
            {
                _selectedCalendarDate = dtPicker.Value.Date;
                MyanmarDateInfo info = MyanmarTraditionalCalendar.FromGregorian(_selectedCalendarDate);
                lblCalOut.Text = $"{info.OfficialGregorianDateText}\n({info.OfficialMyanmarDateText} • {info.WeekdayBurmese}နေ့)";
            }

            dtPicker.ValueChanged += (s, e) => UpdateDateText();
            UpdateDateText();

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(dtPicker, 0, r++);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblCalOut, 0, r++);

            var dateBtnFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            var btnInsertDual = CreatePillButton("+ ရုံးသုံးရက်စွဲ အကွက်ထဲထည့်", Color.FromArgb(232, 246, 237), Color.FromArgb(16, 102, 52), Color.FromArgb(182, 222, 196), _fontBrandStats);
            btnInsertDual.Click += (s, e) =>
            {
                MyanmarDateInfo info = MyanmarTraditionalCalendar.FromGregorian(dtPicker.Value.Date);
                RequestInsertTextIntoCell?.Invoke($"{info.OfficialGregorianDateText} ({info.OfficialMyanmarDateText})");
            };

            var btnCopyDate = CreatePillButton("ရက်စွဲ ကူးယူ (Copy)", Color.FromArgb(242, 245, 250), Color.FromArgb(44, 56, 74), Color.FromArgb(212, 220, 232), _fontBrandStats);
            btnCopyDate.Click += (s, e) =>
            {
                MyanmarDateInfo info = MyanmarTraditionalCalendar.FromGregorian(dtPicker.Value.Date);
                try
                {
                    Clipboard.SetText($"{info.OfficialGregorianDateText} ({info.OfficialMyanmarDateText})");
                    ShowToastBanner("✓ ရုံးသုံးရက်စွဲကို ကူးယူပြီးပါပြီ။");
                }
                catch { }
            };

            dateBtnFlow.Controls.Add(btnInsertDual);
            dateBtnFlow.Controls.Add(btnCopyDate);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(dateBtnFlow, 0, r++);

            return card;
        }
    }
}

