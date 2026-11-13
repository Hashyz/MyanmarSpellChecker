using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    public sealed partial class ExcelProofingTaskPaneControl
    {
        private void SetDictionaryCategory(DictionaryCategoryFilter filter)
        {
            _dictCategoryFilter = filter;
            UpdateDictionaryCategoryStyles();

            if (filter == DictionaryCategoryFilter.EncyclopediaKnowledge && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                var featured = RequestGetFeaturedKnowledge?.Invoke() ?? new List<DictionaryEntry>();
                if (featured.Count > 0)
                {
                    RenderDictionaryResults(
                        UiLocalizer.Pick("Encyclopedia (Featured)", "စွယ်စုံဗဟုသုတ (လေ့လာဖွယ်)"),
                        featured,
                        0.2);
                    return;
                }
            }

            ExecuteDictionarySearchNow();
        }

        private void UpdateDictionaryCategoryStyles()
        {
            ApplyDictChipStyle(_btnDictCatAll, _dictCategoryFilter == DictionaryCategoryFilter.All);
            ApplyDictChipStyle(_btnDictCatThatpon, _dictCategoryFilter == DictionaryCategoryFilter.OfficialThatpon);
            ApplyDictChipStyle(_btnDictCatAbidan, _dictCategoryFilter == DictionaryCategoryFilter.AbidanMeaning);
            ApplyDictChipStyle(_btnDictCatEncyc, _dictCategoryFilter == DictionaryCategoryFilter.EncyclopediaKnowledge);
            ApplyDictChipStyle(_btnDictCatDomain, _dictCategoryFilter == DictionaryCategoryFilter.SpecializedDomains);
            ApplyDictChipStyle(_btnDictCatPropn, _dictCategoryFilter == DictionaryCategoryFilter.ProperNounsAndPlaces);
            ApplyDictChipStyle(_btnDictCatProverb, _dictCategoryFilter == DictionaryCategoryFilter.ProverbsAndSayings);
        }

        private void ApplyDictChipStyle(Button btn, bool selected)
        {
            btn.BackColor = selected ? Color.FromArgb(24, 90, 188) : Color.FromArgb(240, 243, 249);
            btn.ForeColor = selected ? Color.White : Color.FromArgb(48, 58, 74);
            btn.Font = selected ? _fontSmallBold : _fontSmall;
            btn.FlatAppearance.BorderColor = selected ? Color.FromArgb(24, 90, 188) : Color.FromArgb(214, 220, 232);
        }

        private void ExecuteDictionarySearchNow()
        {
            if (_activeTab != TaskPaneWorkspaceTab.Dictionary)
                return;

            string query = _txtDictionarySearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                RenderDictionaryDiscoveryCards();
                return;
            }

            var sw = Stopwatch.StartNew();
            List<DictionaryEntry> filtered;
            if (_dictCategoryFilter == DictionaryCategoryFilter.EncyclopediaKnowledge && RequestSearchReferenceCorpus != null)
            {
                filtered = RequestSearchReferenceCorpus.Invoke(query, false, true);
            }
            else if (_dictCategoryFilter == DictionaryCategoryFilter.ProverbsAndSayings && RequestSearchReferenceCorpus != null)
            {
                filtered = RequestSearchReferenceCorpus.Invoke(query, true, false);
            }
            else
            {
                var raw = RequestDictionarySearch?.Invoke(query) ?? new List<DictionaryEntry>();
                filtered = _dictCategoryFilter switch
                {
                    DictionaryCategoryFilter.OfficialThatpon => raw.Where(e => !e.IsReferenceEntry && e.IsOfficialThatpon).ToList(),
                    DictionaryCategoryFilter.AbidanMeaning => raw.Where(e => !e.IsReferenceEntry && (!string.IsNullOrWhiteSpace(e.MeaningCue) || e.HasAbidanMetadata)).ToList(),
                    DictionaryCategoryFilter.EncyclopediaKnowledge => raw.Where(e => e.IsEncyclopediaEntry).ToList(),
                    DictionaryCategoryFilter.SpecializedDomains => raw.Where(e => e.IsSpecializedDomain).ToList(),
                    DictionaryCategoryFilter.ProperNounsAndPlaces => raw.Where(e => e.IsProperNoun).ToList(),
                    DictionaryCategoryFilter.ProverbsAndSayings => raw.Where(e => e.IsProverbOrSayingEntry).ToList(),
                    _ => raw
                };
            }
            sw.Stop();

            RenderDictionaryResults(query, filtered, sw.Elapsed.TotalMilliseconds);
        }

        private void RenderDictionaryDiscoveryCards()
        {
            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            _lblDictSummary.Text = UiLocalizer.Pick(
                "Instant offline search across Spelling Lexicon, Myanmar Dictionary, Encyclopedia (25,000+) & Proverbs",
                "သတ်ပုံကျမ်း၊ မြန်မာ-မြန်မာ အဘိဓာန်၊ စွယ်စုံဗဟုသုတ (၂၅,၀၀၀+) နှင့် စကားပုံများ အမြန်ရှာဖွေနိုင်သည်");

            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188));
            int r = 0;

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label
            {
                Text = UiLocalizer.Pick(
                    "Myanmar-Myanmar Dictionary & Offline Encyclopedia (Excel)",
                    "မြန်မာ-မြန်မာ အဘိဓာန်ကြီး နှင့် စွယ်စုံဗဟုသုတကျမ်း (Excel Offline)"),
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(20, 52, 108),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Margin = new Padding(0, 0, 0, 5)
            }, 0, r++);

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label
            {
                Text = UiLocalizer.Pick(
                    "• Select any Excel cell and switch to the Dictionary tab to look up its text automatically.\n• Or click any sample topic below to explore —",
                    "• Excel အကွက်တစ်ကွက်ကို ရွေးထားပြီး အဘိဓာန် တက်ဘ်သို့ နှိပ်ပါက ၎င်းစာလုံးကို အလိုအလျောက် ရှာဖွေပေးပါသည်။\n• အောက်ပါ နမူနာခလုတ်များကို နှိပ်၍ စမ်းသပ်ကြည့်ရှုနိုင်ပါသည် —"),
                Font = _fontSmall,
                ForeColor = Color.FromArgb(58, 68, 84),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Margin = new Padding(0, 0, 0, 6)
            }, 0, r++);

            var sampleFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true
            };

            string[] samples = { "ဘဏ္ဍာရေး", "စာရင်းအင်း", "ရသုံးမှန်းခြေငွေစာရင်း", "သံလွင်မြစ်", "မြန်မာသဒ္ဒါ", "ချိုးဖောက်", "ဂျပန်" };
            foreach (string s in samples)
            {
                string captured = s;
                var chip = CreatePillButton(captured, Color.FromArgb(238, 244, 255), Color.FromArgb(22, 74, 158), Color.FromArgb(196, 214, 245), _fontBrandStats);
                chip.Click += (sender, e) => _txtDictionarySearch.Text = captured;
                sampleFlow.Controls.Add(chip);
            }

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(sampleFlow, 0, r++);
            _cardsPanel.Controls.Add(card);

            var featured = RequestGetFeaturedKnowledge?.Invoke() ?? new List<DictionaryEntry>();
            foreach (var entry in featured.Take(5))
            {
                _cardsPanel.Controls.Add(BuildDictionaryEntryCard(entry, cardWidth, innerWrap));
            }

            _cardsPanel.ResumeLayout(true);
        }

        private const int InitialDictionaryRenderLimit = 15;

        private void RenderDictionaryResults(string query, List<DictionaryEntry> entries, double elapsedMs, int maxRender = InitialDictionaryRenderLimit)
        {
            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            _lblDictSummary.Text = entries.Count > 0
                ? UiLocalizer.Pick(
                    $"Found {entries.Count} result(s) for \"{query}\" ({elapsedMs:0.0} ms)",
                    $"\"{query}\" အတွက် ရလဒ် ({entries.Count}) ပုဒ် တွေ့ရှိသည် ({elapsedMs:0.0} ms)")
                : UiLocalizer.Pick(
                    $"No dictionary entries matched \"{query}\"",
                    $"\"{query}\" နှင့် ကိုက်ညီသော အဘိဓာန်ရလဒ် မတွေ့ရှိပါ");

            if (entries.Count == 0)
            {
                var empty = CreateStyledCardContainer(cardWidth, Color.FromArgb(180, 120, 24));
                empty.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                empty.Controls.Add(new Label
                {
                    Text = UiLocalizer.Pick(
                        $"No match for \"{query}\" — try searching by root word or approximate spelling.",
                        $"\"{query}\" ကို မတွေ့ရှိပါ — စာလုံးပေါင်း အနီးစပ်ဆုံး သို့မဟုတ် အရင်းခံစကားလုံးဖြင့် ပြန်လည်ရှာဖွေကြည့်ပါ။"),
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(88, 68, 28),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag
                }, 0, 0);
                _cardsPanel.Controls.Add(empty);
                _cardsPanel.ResumeLayout(true);
                return;
            }

            int takeCount = Math.Min(entries.Count, maxRender);
            for (int i = 0; i < takeCount; i++)
            {
                _cardsPanel.Controls.Add(BuildDictionaryEntryCard(entries[i], cardWidth, innerWrap));
            }

            if (entries.Count > takeCount)
            {
                int remaining = entries.Count - takeCount;
                var moreBtn = CreatePillButton(
                    UiLocalizer.Pick(
                        $"▾ Show ({remaining}) more result(s)",
                        $"▾ နောက်ထပ် ရလဒ် ({remaining}) ပုဒ် ဆက်လက်ပြသရန်"),
                    Color.FromArgb(235, 243, 255),
                    Color.FromArgb(20, 74, 162),
                    Color.FromArgb(190, 212, 246),
                    _fontSmallBold);
                moreBtn.Width = cardWidth;
                moreBtn.Margin = new Padding(0, 2, 0, 8);
                moreBtn.Click += (s, e) => RenderDictionaryResults(query, entries, elapsedMs, takeCount + 30);
                _cardsPanel.Controls.Add(moreBtn);
            }

            _cardsPanel.ResumeLayout(true);
        }

        private Control BuildDictionaryEntryCard(DictionaryEntry entry, int cardWidth, int innerWrap)
        {
            Color stripe = entry.IsEncyclopediaEntry
                ? Color.FromArgb(124, 58, 202)
                : (entry.IsProverbOrSayingEntry ? Color.FromArgb(196, 118, 12) : Color.FromArgb(24, 90, 188));

            var card = CreateStyledCardContainer(cardWidth, stripe);
            int r = 0;

            var topRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72F));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            topRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblHead = new Label
            {
                Text = entry.Word,
                Font = _fontCardWord,
                ForeColor = Color.FromArgb(18, 44, 96),
                AutoSize = true,
                MaximumSize = new Size(Math.Max(120, innerWrap - 90), 0),
                Dock = DockStyle.Fill
            };

            var lblPos = new Label
            {
                Text = entry.LocalizedPosDisplayLabel,
                Font = _fontBrandStats,
                ForeColor = stripe,
                AutoSize = true,
                Dock = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleRight
            };

            topRow.Controls.Add(lblHead, 0, 0);
            topRow.Controls.Add(lblPos, 1, 0);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(topRow, 0, r++);

            if (!string.IsNullOrWhiteSpace(entry.MeaningCue))
            {
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(new Label
                {
                    Text = entry.MeaningCue,
                    Font = _fontBody,
                    ForeColor = Color.FromArgb(30, 40, 56),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Margin = new Padding(0, 0, 0, 4)
                }, 0, r++);
            }

            if (!string.IsNullOrWhiteSpace(entry.FormattedUsageDetails))
            {
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(new Label
                {
                    Text = entry.FormattedUsageDetails,
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(66, 78, 96),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Margin = new Padding(0, 0, 0, 5)
                }, 0, r++);
            }

            string homo = !entry.IsReferenceEntry ? (RequestHomophoneSummary?.Invoke(entry.Word) ?? string.Empty) : string.Empty;
            if (!string.IsNullOrWhiteSpace(homo))
            {
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(new Label
                {
                    Text = UiLocalizer.Pick(
                        $"Homophone Comparison — {homo}",
                        $"သံတူကြောင်းကွဲ နှိုင်းယှဉ်ချက် — {homo}"),
                    Font = _fontBrandStats,
                    ForeColor = Color.FromArgb(148, 86, 8),
                    BackColor = Color.FromArgb(255, 248, 232),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Padding = new Padding(6, 4, 6, 4),
                    Margin = new Padding(0, 0, 0, 5)
                }, 0, r++);
            }

            if (!entry.IsReferenceEntry)
            {
                var syns = RequestGetSynonyms?.Invoke(entry.Word) ?? new List<string>();
                var ants = RequestGetAntonyms?.Invoke(entry.Word) ?? new List<string>();
                if (syns.Count > 0 || ants.Count > 0)
                {
                    var relFlow = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Top,
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        WrapContents = true,
                        Margin = new Padding(0, 0, 0, 4)
                    };
                    foreach (string syn in syns.Take(4))
                    {
                        string cap = syn;
                        var chip = CreatePillButton($"⇄ {cap}", Color.FromArgb(236, 246, 240), Color.FromArgb(16, 102, 52), Color.FromArgb(186, 222, 198), _fontBrandStats);
                        chip.Click += (s, e) => _txtDictionarySearch.Text = cap;
                        relFlow.Controls.Add(chip);
                    }
                    foreach (string ant in ants.Take(3))
                    {
                        string cap = ant;
                        var chip = CreatePillButton($"≠ {cap}", Color.FromArgb(254, 240, 240), Color.FromArgb(168, 32, 32), Color.FromArgb(236, 198, 198), _fontBrandStats);
                        chip.Click += (s, e) => _txtDictionarySearch.Text = cap;
                        relFlow.Controls.Add(chip);
                    }
                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(relFlow, 0, r++);
                }
            }

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true
            };

            string wordToUse = entry.Word;
            var btnCopy = CreatePillButton(
                UiLocalizer.Pick("Copy", "ကူးယူ (Copy)"),
                Color.FromArgb(242, 245, 250),
                Color.FromArgb(44, 56, 74),
                Color.FromArgb(212, 220, 232),
                _fontBrandStats);
            btnCopy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(wordToUse);
                    ShowToastBanner(UiLocalizer.Pick(
                        $"✓ Copied \"{wordToUse}\" to clipboard.",
                        $"✓ \"{wordToUse}\" ကို Clipboard သို့ ကူးယူပြီးပါပြီ။"));
                }
                catch { }
            };

            var btnInsert = CreatePillButton(
                UiLocalizer.Pick("+ Insert into Cell", "+ အကွက်ထဲထည့်မည်"),
                Color.FromArgb(232, 246, 237),
                Color.FromArgb(16, 102, 52),
                Color.FromArgb(182, 222, 196),
                _fontBrandStats);
            btnInsert.Click += (s, e) => RequestInsertTextIntoCell?.Invoke(wordToUse);

            btnRow.Controls.Add(btnCopy);
            btnRow.Controls.Add(btnInsert);

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnRow, 0, r++);

            return card;
        }
    }
}
