using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.WordAddIn.Interop;

namespace MyanmarSpellChecker.WordAddIn.UI
{
    public sealed partial class ProofingTaskPaneControl
    {
        // ================= DICTIONARY & PROVERBS EXPLORER =================

        private void SetDictionaryCategory(DictionaryCategoryFilter filter)
        {
            _dictCategoryFilter = filter;
            UpdateDictionaryCategoryStyles();

            if (filter == DictionaryCategoryFilter.EncyclopediaKnowledge && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                var featured = RequestGetFeaturedKnowledge?.Invoke() ?? new List<DictionaryEntry>();
                if (featured.Count > 0)
                {
                    RenderDictionarySearchResults("စွယ်စုံဗဟုသုတ (ကျပန်းလေ့လာဖွယ်)", featured, 0.2);
                    return;
                }
                _txtDictionarySearch.Text = "မြန်မာသဒ္ဒါ";
                return;
            }
            if (filter == DictionaryCategoryFilter.ProverbsAndSayings && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                _txtDictionarySearch.Text = "အ";
                return;
            }
            if (filter == DictionaryCategoryFilter.SpecializedDomains && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                _txtDictionarySearch.Text = "ဥပဒေ";
                return;
            }
            if (filter == DictionaryCategoryFilter.ProperNounsAndPlaces && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                _txtDictionarySearch.Text = "နိုင်ငံ";
                return;
            }

            ExecuteDictionarySearchNow();
        }

        private void UpdateDictionaryCategoryStyles()
        {
            ApplyChipStyle(_btnDictCatAll, _dictCategoryFilter == DictionaryCategoryFilter.All);
            ApplyChipStyle(_btnDictCatThatpon, _dictCategoryFilter == DictionaryCategoryFilter.OfficialThatpon);
            ApplyChipStyle(_btnDictCatAbidan, _dictCategoryFilter == DictionaryCategoryFilter.AbidanMeaning);
            ApplyChipStyle(_btnDictCatEncyc, _dictCategoryFilter == DictionaryCategoryFilter.EncyclopediaKnowledge);
            ApplyChipStyle(_btnDictCatDomain, _dictCategoryFilter == DictionaryCategoryFilter.SpecializedDomains);
            ApplyChipStyle(_btnDictCatPropn, _dictCategoryFilter == DictionaryCategoryFilter.ProperNounsAndPlaces);
            ApplyChipStyle(_btnDictCatProverb, _dictCategoryFilter == DictionaryCategoryFilter.ProverbsAndSayings);
        }

        private void ApplyChipStyle(Button btn, bool selected)
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
                RenderDictionaryDiscoveryView();
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
                List<DictionaryEntry> rawMatches = RequestDictionarySearch?.Invoke(query) ?? new List<DictionaryEntry>();
                filtered = rawMatches;

                switch (_dictCategoryFilter)
                {
                    case DictionaryCategoryFilter.OfficialThatpon:
                        filtered = rawMatches.Where(e => !e.IsReferenceEntry && e.IsOfficialThatpon).ToList();
                        break;
                    case DictionaryCategoryFilter.AbidanMeaning:
                        filtered = rawMatches.Where(e => !e.IsReferenceEntry && (!string.IsNullOrWhiteSpace(e.MeaningCue) || e.HasAbidanMetadata)).ToList();
                        break;
                    case DictionaryCategoryFilter.EncyclopediaKnowledge:
                        filtered = rawMatches.Where(e => e.IsEncyclopediaEntry).ToList();
                        break;
                    case DictionaryCategoryFilter.SpecializedDomains:
                        filtered = rawMatches.Where(e => e.IsSpecializedDomain).ToList();
                        break;
                    case DictionaryCategoryFilter.ProperNounsAndPlaces:
                        filtered = rawMatches.Where(e => e.IsProperNoun).ToList();
                        break;
                    case DictionaryCategoryFilter.ProverbsAndSayings:
                        filtered = rawMatches.Where(e => e.IsProverbOrSayingEntry).ToList();
                        break;
                }
            }
            sw.Stop();

            RenderDictionarySearchResults(query, filtered, sw.Elapsed.TotalMilliseconds);
        }

        private void RenderDictionaryDiscoveryView()
        {
            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            _lblDictSummary.Text = "မြန်မာ-မြန်မာ အဘိဓာန်ကြီး၊ စွယ်စုံဗဟုသုတကျမ်း (၂၅,၀၀၀+)၊ နိုင်ငံ/ပညာရပ်နှင့် စကားပုံများ အမြန်ရှာဖွေနိုင်သည်";
            _lblDictSummary.ForeColor = Color.FromArgb(24, 82, 172);

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188), isSelectableItem: null);
            int row = 0;

            var lblGuideTitle = new Label
            {
                Text = "မြန်မာ-မြန်မာ အဘိဓာန်ကြီး နှင့် စွယ်စုံဗဟုသုတကျမ်း လမ်းညွှန် (100% Offline)",
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(20, 52, 108),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblGuideTitle, 0, row++);

            var lblGuideBody = new Label
            {
                Text = "• မြန်မာ-မြန်မာ အနက်အဓိပ္ပာယ်အပြည့်အစုံ၊ ကြောင်းတူသံကွဲ (⇄)၊ ဆန့်ကျင်ဘက် (≠)၊ အသံထွက်၊ ရင်းမြစ်၊ သာဓက နှင့် ဆက်စပ်ဝေါဟာရ (→) များကို တစ်နေရာတည်း လေ့လာနိုင်ပါသည်။\n• သိပ္ပံ၊ ဆေးပညာ၊ သမိုင်း၊ စာပေ၊ သဒ္ဒါ၊ ဗုဒ္ဓဘာသာ၊ ဥပဒေ နှင့် ကမ္ဘာ့နိုင်ငံစွယ်စုံ (၂၄၅+) အပါအဝင် စွယ်စုံဗဟုသုတ ဆောင်းပါးပေါင်း (၂၅,၀၀၀+) ကို အင်တာနက်မလိုဘဲ ရှာဖွေဖတ်ရှုနိုင်ပါသည် —",
                Font = _fontSmall,
                ForeColor = Color.FromArgb(58, 68, 84),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblGuideBody, 0, row++);

            var btnExploreRandom = CreatePillButton(
                "🎲 ဗဟုသုတ နှင့် အဘိဓာန် ကျပန်းဖတ်ရှုရန် (Explore Knowledge)",
                Color.FromArgb(232, 248, 246),
                Color.FromArgb(12, 96, 92),
                Color.FromArgb(168, 222, 214),
                _fontSmallBold);
            btnExploreRandom.Margin = new Padding(2, 0, 2, 7);
            btnExploreRandom.Click += (s, e) =>
            {
                var featured = RequestGetFeaturedKnowledge?.Invoke() ?? new List<DictionaryEntry>();
                if (featured.Count > 0)
                {
                    RenderDictionarySearchResults("ကျပန်း လေ့လာဖွယ် စွယ်စုံဗဟုသုတ နှင့် အဘိဓာန်", featured, 0.2);
                }
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnExploreRandom, 0, row++);

            string[] starterWords = { "မြန်မာသဒ္ဒါ", "ကချင်ပြည်နယ်", "ကင်ဆာရောဂါ", "Procurement", "ဆွစ်ဇာလန်", "ကုလသမဂ္ဂ", "ပိဿာ", "ပန်းဆယ်မျိုး" };
            var chipGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 4,
                Margin = new Padding(0)
            };
            chipGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            chipGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            for (int r = 0; r < 4; r++)
            {
                chipGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            for (int i = 0; i < starterWords.Length; i++)
            {
                string sample = starterWords[i];
                var chip = CreatePillButton(
                    $"🔍 {sample}",
                    Color.FromArgb(240, 245, 255),
                    Color.FromArgb(22, 72, 155),
                    Color.FromArgb(196, 214, 245),
                    _fontSmall);
                chip.Click += (s, e) =>
                {
                    _dictCategoryFilter = DictionaryCategoryFilter.All;
                    UpdateDictionaryCategoryStyles();
                    _txtDictionarySearch.Text = sample;
                    ExecuteDictionarySearchNow();
                };
                chipGrid.Controls.Add(chip, i % 2, i / 2);
            }

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(chipGrid, 0, row++);

            _cardsPanel.Controls.Add(card);
            _cardsPanel.ResumeLayout();
        }

        private void RenderDictionarySearchResults(string query, List<DictionaryEntry> matches, double elapsedMs = 0.0)
        {
            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            string speedSuffix = elapsedMs > 0.0 ? $" ({elapsedMs:0.0} ms)" : string.Empty;
            _lblDictSummary.Text = matches.Count > 0
                ? $"\"{query}\" အတွက် ရလဒ် ({matches.Count}) ခု တွေ့ရှိသည်{speedSuffix}"
                : $"\"{query}\" နှင့် ကိုက်ညီသော ရလဒ် မတွေ့ရှိပါ{speedSuffix}";
            _lblDictSummary.ForeColor = matches.Count > 0
                ? Color.FromArgb(20, 85, 185)
                : Color.FromArgb(165, 65, 20);

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            if (matches.Count == 0)
            {
                var emptyCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(215, 130, 30), isSelectableItem: null);
                var lblEmpty = new Label
                {
                    Text = $"\"{query}\" နှင့် တိုက်ရိုက်ကိုက်ညီသော ဝေါဟာရ မတွေ့ရှိသေးပါ။ စာလုံးအစိတ်အပိုင်း သို့မဟုတ် အသံထွက်တူ စာလုံးဖြင့် ပြန်လည်ရှာဖွေကြည့်ပါ။",
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(70, 78, 92),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill
                };
                emptyCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                emptyCard.Controls.Add(lblEmpty, 0, 0);
                _cardsPanel.Controls.Add(emptyCard);
                _cardsPanel.ResumeLayout();
                return;
            }

            foreach (var entry in matches)
            {
                bool isEncyc = entry.IsEncyclopediaEntry;
                bool isProverb = entry.IsProverbOrSayingEntry;
                bool isRefPhrase = entry.IsReferenceEntry;
                bool isPropn = entry.IsProperNoun;
                bool isDomain = entry.IsSpecializedDomain;
                Color accent = isEncyc
                    ? Color.FromArgb(16, 108, 128)
                    : (isProverb
                        ? Color.FromArgb(182, 102, 12)
                        : (isDomain
                            ? Color.FromArgb(88, 52, 168)
                            : (isPropn
                                ? Color.FromArgb(14, 118, 110)
                                : (entry.IsOfficialThatpon ? Color.FromArgb(24, 132, 68) : Color.FromArgb(28, 96, 188)))));

                var card = CreateStyledCardContainer(cardWidth, accent, isSelectableItem: null);
                int dRow = 0;

                string sourceBadge = isEncyc
                    ? (string.IsNullOrEmpty(entry.SpecializedDomain) ? "စွယ်စုံဗဟုသုတကျမ်း ✓" : $"စွယ်စုံဗဟုသုတ ({entry.SpecializedDomain}) ✓")
                    : (isProverb
                        ? "မြန်မာစာအဖွဲ့ စံကိုးကား ✓"
                        : (isDomain
                            ? $"ပညာရပ်ဝေါဟာရ ({entry.SpecializedDomain}) ✓"
                            : (isPropn
                                ? "ပထဝီဝင် / ရုံးသုံးအမည်မှန် ✓"
                                : (entry.IsOfficialThatpon
                                    ? "သတ်ပုံကျမ်းဝင် ✓"
                                    : (string.Equals(entry.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase)
                                        ? "မှတ်သားဝေါဟာရ ★"
                                        : (string.Equals(entry.PosTag, "LOAN", StringComparison.OrdinalIgnoreCase) ? "မွေးစားဝေါဟာရ" : "ခေတ်သုံး စံဝေါဟာရ"))))));

                var lblWord = new Label
                {
                    Text = $"{entry.Word}   [{entry.PosDisplayLabel}]",
                    Font = _fontCardWord,
                    ForeColor = isEncyc
                        ? Color.FromArgb(10, 78, 96)
                        : (isProverb
                            ? Color.FromArgb(128, 68, 8)
                            : (isDomain
                                ? Color.FromArgb(62, 32, 128)
                                : (isPropn ? Color.FromArgb(12, 88, 82) : Color.FromArgb(18, 48, 106)))),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 2),
                    Padding = new Padding(2, 2, 2, 2)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblWord, 0, dRow++);

                string metaText = isRefPhrase
                    ? sourceBadge
                    : $"{sourceBadge}   •   ဝဏ္ဏခွဲ: {OrthographicDiffExplainer.FormatSyllableBreakdown(entry.Word)}";
                var lblMeta = new Label
                {
                    Text = metaText,
                    Font = _fontSmallBold,
                    ForeColor = accent,
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 4),
                    Padding = new Padding(2, 1, 2, 1)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblMeta, 0, dRow++);

                string cueLabelPrefix = isEncyc
                    ? "စွယ်စုံဗဟုသုတ အနှစ်ချုပ်"
                    : (isProverb
                        ? "ဆိုလိုရင်း အဓိပ္ပာယ်"
                        : (isDomain
                            ? "ပညာရပ် အနက် / အသုံး"
                            : (isPropn ? "ပထဝီဝင် / အချက်အလက်" : "မြန်မာအဘိဓာန် အနက် / အသုံး")));
                var lblCue = new Label
                {
                    Text = string.IsNullOrWhiteSpace(entry.MeaningCue)
                        ? "အနက် / အသုံး: မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း စံစာလုံးပေါင်း"
                        : $"{cueLabelPrefix}: {entry.MeaningCue}",
                    Font = _fontBody,
                    ForeColor = Color.FromArgb(42, 52, 68),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 5),
                    Padding = new Padding(2, 2, 2, 2)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblCue, 0, dRow++);

                if (entry.HasAbidanMetadata)
                {
                    string fullMetaText = entry.FormattedUsageDetails;
                    string[] metaLines = fullMetaText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    bool isCollapsibleMeta = metaLines.Length >= 5;
                    string collapsedMetaText = isCollapsibleMeta
                        ? string.Join("\n", metaLines.Take(3))
                        : fullMetaText;

                    var lblAbidanMeta = new Label
                    {
                        Text = collapsedMetaText,
                        Font = _fontSmall,
                        ForeColor = Color.FromArgb(30, 58, 92),
                        BackColor = Color.FromArgb(243, 248, 255),
                        AutoSize = true,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(0, 0, 0, isCollapsibleMeta ? 2 : 5),
                        Padding = new Padding(8, 5, 8, 5)
                    };
                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(lblAbidanMeta, 0, dRow++);

                    if (isCollapsibleMeta)
                    {
                        bool metaExpanded = false;
                        var lblToggleMeta = new Label
                        {
                            Text = $"▾ အချက်အလက် အပြည့်အစုံကြည့်ရန် ({metaLines.Length} ချက်)",
                            Font = _fontSmallBold,
                            ForeColor = Color.FromArgb(22, 82, 172),
                            AutoSize = true,
                            Dock = DockStyle.Fill,
                            Margin = new Padding(2, 0, 2, 5),
                            Cursor = Cursors.Hand
                        };
                        lblToggleMeta.Click += (s, e) =>
                        {
                            metaExpanded = !metaExpanded;
                            _cardsPanel.SuspendLayout();
                            lblAbidanMeta.Text = metaExpanded ? fullMetaText : collapsedMetaText;
                            lblToggleMeta.Text = metaExpanded
                                ? "▴ အကျဉ်းချုပ် ပြန်ပိတ်ရန်"
                                : $"▾ အချက်အလက် အပြည့်အစုံကြည့်ရန် ({metaLines.Length} ချက်)";
                            _cardsPanel.ResumeLayout();
                        };
                        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        card.Controls.Add(lblToggleMeta, 0, dRow++);
                    }
                }

                List<string> synonyms = isRefPhrase
                    ? new List<string>()
                    : (RequestGetSynonyms?.Invoke(entry.Word) ?? new List<string>());
                if (synonyms.Count > 0)
                {
                    var lblSynHeader = new Label
                    {
                        Text = "ကြောင်းတူသံကွဲ (နှိပ်လျှင် ဆက်လက်ရှာဖွေမည်) —",
                        Font = _fontSmallBold,
                        ForeColor = Color.FromArgb(16, 102, 82),
                        AutoSize = true,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(2, 0, 2, 2)
                    };
                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(lblSynHeader, 0, dRow++);

                    var synFlow = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Top,
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        WrapContents = true,
                        FlowDirection = FlowDirection.LeftToRight,
                        Margin = new Padding(0, 0, 0, 6),
                        Padding = new Padding(0)
                    };

                    foreach (string syn in synonyms)
                    {
                        string targetSyn = syn;
                        var synPill = CreatePillButton(
                            $"⇄ {targetSyn}",
                            Color.FromArgb(234, 249, 244),
                            Color.FromArgb(14, 96, 76),
                            Color.FromArgb(178, 224, 208),
                            _fontSmall);
                        synPill.Dock = DockStyle.None;
                        _toolTip.SetToolTip(synPill, $"\"{targetSyn}\" ၏ အနက်နှင့် အသုံးကို ဆက်လက်ရှာဖွေရန် နှိပ်ပါ");
                        synPill.Click += (s, e) => OpenDictionaryWithQuery(targetSyn);
                        synFlow.Controls.Add(synPill);
                    }

                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(synFlow, 0, dRow++);
                }

                List<string> antonyms = isRefPhrase
                    ? new List<string>()
                    : (RequestGetAntonyms?.Invoke(entry.Word) ?? new List<string>());
                if (antonyms.Count > 0)
                {
                    var lblAntHeader = new Label
                    {
                        Text = "ဆန့်ကျင်ဘက် ဝေါဟာရ (နှိပ်လျှင် ဆက်လက်ရှာဖွေမည်) —",
                        Font = _fontSmallBold,
                        ForeColor = Color.FromArgb(162, 42, 62),
                        AutoSize = true,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(2, 0, 2, 2)
                    };
                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(lblAntHeader, 0, dRow++);

                    var antFlow = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Top,
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        WrapContents = true,
                        FlowDirection = FlowDirection.LeftToRight,
                        Margin = new Padding(0, 0, 0, 6),
                        Padding = new Padding(0)
                    };

                    foreach (string ant in antonyms)
                    {
                        string targetAnt = ant;
                        var antPill = CreatePillButton(
                            $"≠ {targetAnt}",
                            Color.FromArgb(255, 241, 243),
                            Color.FromArgb(158, 36, 56),
                            Color.FromArgb(242, 196, 204),
                            _fontSmall);
                        antPill.Dock = DockStyle.None;
                        _toolTip.SetToolTip(antPill, $"\"{targetAnt}\" ၏ အနက်နှင့် အသုံးကို ဆက်လက်ရှာဖွေရန် နှိပ်ပါ");
                        antPill.Click += (s, e) => OpenDictionaryWithQuery(targetAnt);
                        antFlow.Controls.Add(antPill);
                    }

                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(antFlow, 0, dRow++);
                }

                if (!string.IsNullOrWhiteSpace(entry.RelatedTerms))
                {
                    string[] relItems = entry.RelatedTerms
                        .Split(new[] { '၊', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length >= 2)
                        .Take(6)
                        .ToArray();
                    if (relItems.Length > 0)
                    {
                        var lblRelHeader = new Label
                        {
                            Text = "ဆက်စပ်ဖတ်ရှုဖွယ် ဗဟုသုတ / ဝေါဟာရ —",
                            Font = _fontSmallBold,
                            ForeColor = Color.FromArgb(24, 92, 118),
                            AutoSize = true,
                            Dock = DockStyle.Fill,
                            Margin = new Padding(2, 0, 2, 2)
                        };
                        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        card.Controls.Add(lblRelHeader, 0, dRow++);

                        var relFlow = new FlowLayoutPanel
                        {
                            Dock = DockStyle.Top,
                            AutoSize = true,
                            AutoSizeMode = AutoSizeMode.GrowAndShrink,
                            MaximumSize = new Size(innerWrap, 0),
                            Tag = WrapTag,
                            WrapContents = true,
                            FlowDirection = FlowDirection.LeftToRight,
                            Margin = new Padding(0, 0, 0, 6),
                            Padding = new Padding(0)
                        };

                        foreach (string rel in relItems)
                        {
                            string targetRel = rel;
                            var relPill = CreatePillButton(
                                $"→ {targetRel}",
                                Color.FromArgb(236, 247, 252),
                                Color.FromArgb(16, 86, 112),
                                Color.FromArgb(182, 218, 234),
                                _fontSmall);
                            relPill.Dock = DockStyle.None;
                            _toolTip.SetToolTip(relPill, $"\"{targetRel}\" အကြောင်း ဆက်လက်ဖတ်ရှုရန် နှိပ်ပါ");
                            relPill.Click += (s, e) => OpenDictionaryWithQuery(targetRel);
                            relFlow.Controls.Add(relPill);
                        }

                        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        card.Controls.Add(relFlow, 0, dRow++);
                    }
                }

                string homophones = isRefPhrase ? string.Empty : (RequestHomophoneSummary?.Invoke(entry.Word) ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(homophones))
                {
                    var lblHomo = new Label
                    {
                        Text = $"အသံတူ/ဆင်တူ သတိပြုရန်: {homophones}",
                        Font = _fontSmall,
                        ForeColor = Color.FromArgb(145, 82, 12),
                        BackColor = Color.FromArgb(255, 250, 238),
                        AutoSize = true,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(0, 0, 0, 6),
                        Padding = new Padding(7, 4, 7, 4)
                    };
                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(lblHomo, 0, dRow++);
                }

                var dictActions = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    RowCount = 1,
                    Margin = new Padding(0, 2, 0, 0)
                };
                dictActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
                dictActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
                dictActions.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                string wordToUse = entry.Word;

                var btnCopy = CreatePillButton("ကူးယူမည်", Color.FromArgb(242, 245, 250), Color.FromArgb(48, 58, 74), Color.FromArgb(215, 222, 232), _fontSmall);
                btnCopy.Click += (s, e) =>
                {
                    try
                    {
                        Clipboard.SetText(wordToUse);
                        btnCopy.Text = "✓ ကူးယူပြီး";
                        btnCopy.ForeColor = Color.FromArgb(18, 120, 52);
                    }
                    catch { }
                };

                var btnInsert = CreatePillButton(
                    "စာတမ်းသို့ ထည့်မည် ➔",
                    Color.FromArgb(235, 244, 255),
                    Color.FromArgb(20, 68, 148),
                    Color.FromArgb(190, 212, 246),
                    _fontSmallBold);
                btnInsert.Click += (s, e) => RequestInsertWordAtCursor?.Invoke(wordToUse);

                dictActions.Controls.Add(btnCopy, 0, 0);
                dictActions.Controls.Add(btnInsert, 1, 0);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(dictActions, 0, dRow++);

                _cardsPanel.Controls.Add(card);
            }

            _cardsPanel.ResumeLayout();
        }
    }
}
