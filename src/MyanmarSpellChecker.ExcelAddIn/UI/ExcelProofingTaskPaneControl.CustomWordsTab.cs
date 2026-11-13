using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    public sealed partial class ExcelProofingTaskPaneControl
    {
        private void AddNewCustomWordFromInput()
        {
            string word = _txtNewCustomWord.Text.Trim();
            if (string.IsNullOrWhiteSpace(word))
                return;

            bool ok = RequestAddCustomWordManual?.Invoke(word) ?? false;
            if (ok)
            {
                _txtNewCustomWord.Clear();
                ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Added \"{word}\" to your Personal Dictionary (Word + Excel).",
                    $"✓ \"{word}\" ကို ကိုယ်ပိုင်အဘိဓာန် (Word + Excel) သို့ မှတ်သားပြီးပါပြီ။"));
                RenderCustomWordsWorkspace();
            }
            else
            {
                ShowToastBanner(UiLocalizer.Pick(
                    $"⚠ \"{word}\" is already saved or does not contain Myanmar script.",
                    $"⚠ \"{word}\" သည် မှတ်သားပြီးဖြစ်သည် သို့မဟုတ် မြန်မာစာလုံး မဟုတ်ပါ။"), isSuccess: false);
            }
        }

        private void RenderCustomWordsWorkspace()
        {
            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            IReadOnlyList<string> words = RequestGetCustomWords?.Invoke() ?? Array.Empty<string>();
            _lblCustomSummary.Text = UiLocalizer.Pick(
                $"Saved personal words ({words.Count}) — shared across Word & Excel",
                $"မှတ်သားထားသော ဝေါဟာရ ({words.Count}) လုံး — Word နှင့် Excel နှစ်မျိုးလုံး အကျုံးဝင်သည်");

            if (words.Count == 0)
            {
                var emptyCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188));
                emptyCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                emptyCard.Controls.Add(new Label
                {
                    Text = UiLocalizer.Pick(
                        "No custom Myanmar words saved yet.\nEnter person names, organization names, abbreviations, or specialized terms above so they won't be flagged in Word or Excel.",
                        "မှတ်သားထားသော ကိုယ်ပိုင်ဝေါဟာရ မရှိသေးပါ။\nအထက်ပါအကွက်တွင် လူအမည်၊ ကုမ္ပဏီအမည်၊ ဌာနအတိုကောက် သို့မဟုတ် အထူးဝေါဟာရများကို ရိုက်ထည့်မှတ်သားနိုင်ပါသည်။"),
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(64, 76, 94),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag
                }, 0, 0);
                _cardsPanel.Controls.Add(emptyCard);
                _cardsPanel.ResumeLayout(true);
                return;
            }

            foreach (string w in words)
            {
                var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(16, 124, 65));
                var rowGrid = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    RowCount = 1
                };
                rowGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 74F));
                rowGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
                rowGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var lblWord = new Label
                {
                    Text = w,
                    Font = _fontBodyBold,
                    ForeColor = Color.FromArgb(22, 34, 52),
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Padding = new Padding(0, 4, 0, 2)
                };

                string captured = w;
                var btnRemove = CreatePillButton(
                    UiLocalizer.Pick("Remove", "ဖျက်မည်"),
                    Color.FromArgb(253, 238, 238),
                    Color.FromArgb(175, 28, 28),
                    Color.FromArgb(236, 198, 198),
                    _fontBrandStats);
                btnRemove.Dock = DockStyle.Right;
                btnRemove.Click += (s, e) =>
                {
                    if (RequestRemoveCustomWord?.Invoke(captured) == true)
                    {
                        ShowToastBanner(UiLocalizer.Pick(
                            $"Removed \"{captured}\" from Personal Dictionary.",
                            $"\"{captured}\" ကို ကိုယ်ပိုင်စာလုံးစာရင်းမှ ဖယ်ရှားပြီးပါပြီ။"));
                        RenderCustomWordsWorkspace();
                    }
                };

                rowGrid.Controls.Add(lblWord, 0, 0);
                rowGrid.Controls.Add(btnRemove, 1, 0);
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(rowGrid, 0, 0);
                _cardsPanel.Controls.Add(card);
            }

            _cardsPanel.ResumeLayout(true);
        }
    }
}

