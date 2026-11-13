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
        // ================= CUSTOM DICTIONARY MANAGER =================

        private void AddManualCustomWordFromInput()
        {
            string candidate = _txtNewCustomWord.Text.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            bool ok = RequestAddCustomWordManual?.Invoke(candidate) ?? false;
            if (ok)
            {
                _txtNewCustomWord.Text = string.Empty;
                ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Saved \"{candidate}\" to your Custom Dictionary.",
                    $"✓ \"{candidate}\" ကို ကိုယ်ပိုင်ဝေါဟာရအဖြစ် သိမ်းဆည်းပြီးပါပြီ။"));
                UpdateWorkspaceTabStyles();
                RenderCustomWordsView();
            }
            else
            {
                ShowToastBanner(
                    UiLocalizer.Pick(
                        "⚠ Please enter a valid Myanmar Unicode word.",
                        "⚠ မှန်ကန်သော မြန်မာယူနီကုဒ် စာလုံးကိုသာ ရိုက်ထည့်ပေးပါ။"),
                    isSuccess: false);
            }
        }

        private void RenderCustomWordsView()
        {
            if (_activeTab != TaskPaneWorkspaceTab.CustomWords)
                return;

            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            IReadOnlyList<string> customWords = RequestGetCustomWords?.Invoke() ?? Array.Empty<string>();
            _lblCustomSummary.Text = customWords.Count > 0
                ? UiLocalizer.Pick(
                    $"Saved Custom / Organization Words ({customWords.Count})",
                    $"မှတ်သားထားသော ကိုယ်ပိုင်/ရုံးသုံး ဝေါဟာရ ({customWords.Count}) လုံး")
                : UiLocalizer.Pick(
                    "No custom words saved yet",
                    "မှတ်သားထားသော ကိုယ်ပိုင်ဝေါဟာရ မရှိသေးပါ");

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            if (customWords.Count == 0)
            {
                var infoCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188), isSelectableItem: null);
                var lblInfo = new Label
                {
                    Text = UiLocalizer.Pick(
                        "Add personal names, company/ministry names, or specialized terminology in the input box above.\n\nSaved custom words are never flagged as spelling errors during proofing and can be removed here anytime.",
                        "လူပုဂ္ဂိုလ်အမည်၊ ကုမ္ပဏီ/ဌာနအမည် သို့မဟုတ် အထူးသုံးဝေါဟာရများကို အပေါ်ရှိ အကွက်တွင် ရိုက်ထည့်မှတ်သားနိုင်ပါသည်။\n\nမှတ်သားထားသော စာလုံးများကို စာတမ်းစစ်ဆေးသည့်အခါ အမှားအဖြစ် မတားတော့ဘဲ၊ အချိန်မရွေး ဤနေရာမှ ပြန်လည်ဖျက်ပစ်နိုင်ပါသည်။"),
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(58, 68, 84),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill
                };
                infoCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                infoCard.Controls.Add(lblInfo, 0, 0);
                _cardsPanel.Controls.Add(infoCard);
                _cardsPanel.ResumeLayout();
                return;
            }

            foreach (string word in customWords)
            {
                var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(28, 102, 192), isSelectableItem: null);
                int r = 0;

                var lblWord = new Label
                {
                    Text = word,
                    Font = _fontCardWord,
                    ForeColor = Color.FromArgb(20, 48, 102),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 2)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblWord, 0, r++);

                var lblBreakdown = new Label
                {
                    Text = UiLocalizer.Pick(
                        $"Syllables: {OrthographicDiffExplainer.FormatSyllableBreakdown(word)}  •  Custom Word",
                        $"ဝဏ္ဏခွဲ: {OrthographicDiffExplainer.FormatSyllableBreakdown(word)}  •  မှတ်သားဝေါဟာရ"),
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(88, 98, 114),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 6)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblBreakdown, 0, r++);

                var actions = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    RowCount = 1,
                    Margin = new Padding(0)
                };
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
                actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                string capturedWord = word;
                var btnInsert = CreatePillButton(
                    UiLocalizer.Pick("Insert in Doc", "စာတမ်းသို့ ထည့်မည်"),
                    Color.FromArgb(236, 244, 255), Color.FromArgb(20, 72, 155), Color.FromArgb(190, 212, 246), _fontSmall);
                btnInsert.Click += (s, e) => RequestInsertWordAtCursor?.Invoke(capturedWord);

                var btnDelete = CreatePillButton(
                    UiLocalizer.Pick("Remove ✕", "ဖျက်မည် ✕"),
                    Color.FromArgb(253, 238, 236), Color.FromArgb(175, 30, 30), Color.FromArgb(242, 204, 202), _fontSmallBold);
                btnDelete.Click += (s, e) =>
                {
                    if (RequestRemoveCustomWord?.Invoke(capturedWord) == true)
                    {
                        ShowToastBanner(UiLocalizer.Pick(
                            $"Removed \"{capturedWord}\" from your Custom Dictionary.",
                            $"\"{capturedWord}\" ကို မှတ်သားဝေါဟာရမှ ဖယ်ရှားပြီးပါပြီ။"));
                        UpdateWorkspaceTabStyles();
                        RenderCustomWordsView();
                    }
                };

                actions.Controls.Add(btnInsert, 0, 0);
                actions.Controls.Add(btnDelete, 1, 0);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(actions, 0, r++);

                _cardsPanel.Controls.Add(card);
            }

            _cardsPanel.ResumeLayout();
        }
    }
}
