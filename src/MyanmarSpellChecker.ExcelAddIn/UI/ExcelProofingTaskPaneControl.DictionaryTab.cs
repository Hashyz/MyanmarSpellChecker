using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    public sealed partial class ExcelProofingTaskPaneControl
    {
        private void PushDictionaryHistory(string query)
        {
            string cleaned = (query ?? string.Empty).Trim();
            if (cleaned.Length == 0)
                return;

            if (_dictHistoryIndex >= 0 &&
                _dictHistoryIndex < _dictHistory.Count &&
                string.Equals(_dictHistory[_dictHistoryIndex], cleaned, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_dictHistoryIndex < _dictHistory.Count - 1)
            {
                _dictHistory.RemoveRange(_dictHistoryIndex + 1, _dictHistory.Count - _dictHistoryIndex - 1);
            }

            _dictHistory.Add(cleaned);
            if (_dictHistory.Count > 60)
            {
                _dictHistory.RemoveAt(0);
            }
            _dictHistoryIndex = _dictHistory.Count - 1;
        }

        private void NavigateDictionaryHistory(int delta)
        {
            int nextIndex = _dictHistoryIndex + delta;
            if (nextIndex < 0 || nextIndex >= _dictHistory.Count)
                return;

            _dictHistoryIndex = nextIndex;
            _showingSavedBookmarksView = false;
            _suppressDictHistoryPush = true;
            try
            {
                _searchDebounceTimer.Stop();
                _txtDictionarySearch.Text = _dictHistory[_dictHistoryIndex];
                _searchDebounceTimer.Stop();
                ExecuteDictionarySearchNow();
            }
            finally
            {
                _suppressDictHistoryPush = false;
            }
        }

        private void UpdateDictionaryNavButtons()
        {
            if (_btnDictBack == null || _btnDictForward == null || _btnDictSaved == null)
                return;

            bool canBack = _dictHistoryIndex > 0;
            bool canFwd = _dictHistoryIndex >= 0 && _dictHistoryIndex < _dictHistory.Count - 1;

            _btnDictBack.Enabled = canBack;
            _btnDictForward.Enabled = canFwd;
            _btnDictBack.Text = UiLocalizer.Pick("← Back", "← ရှေ့");
            _btnDictForward.Text = UiLocalizer.Pick("Next →", "နောက် →");
            _btnDictBack.ForeColor = canBack ? Color.FromArgb(22, 74, 158) : Color.FromArgb(148, 156, 168);
            _btnDictForward.ForeColor = canFwd ? Color.FromArgb(22, 74, 158) : Color.FromArgb(148, 156, 168);

            int savedCount = DictionaryBookmarkStore.Shared.Count;
            _btnDictSaved.Text = UiLocalizer.Pick($"★ Saved ({savedCount})", $"★ မှတ်သား ({savedCount})");
            _btnDictSaved.BackColor = _showingSavedBookmarksView
                ? Color.FromArgb(196, 118, 12)
                : Color.FromArgb(255, 248, 230);
            _btnDictSaved.ForeColor = _showingSavedBookmarksView
                ? Color.White
                : Color.FromArgb(148, 86, 8);
            _btnDictSaved.FlatAppearance.BorderColor = _showingSavedBookmarksView
                ? Color.FromArgb(196, 118, 12)
                : Color.FromArgb(242, 214, 152);

            _toolTip.SetToolTip(
                _btnDictBack,
                UiLocalizer.Pick("Previous dictionary lookup", "ရှေ့တွင် ရှာဖွေခဲ့သော ဝေါဟာရသို့ ပြန်သွားရန်"));
            _toolTip.SetToolTip(
                _btnDictForward,
                UiLocalizer.Pick("Next dictionary lookup", "နောက်တွင် ရှာဖွေခဲ့သော ဝေါဟာရသို့ သွားရန်"));
            _toolTip.SetToolTip(
                _btnDictSaved,
                UiLocalizer.Pick(
                    "View bookmarked Myanmar & English dictionary entries (synced across Word & Excel)",
                    "မှတ်သားထားသော မြန်မာ/အင်္ဂလိပ် အဘိဓာန်ဝေါဟာရများ (Word နှင့် Excel အတူတကွ ချိတ်ဆက်ထားသည်)"));
        }

        private void ToggleSavedBookmarksView()
        {
            _showingSavedBookmarksView = !_showingSavedBookmarksView;
            UpdateDictionaryNavButtons();
            if (_showingSavedBookmarksView)
            {
                RenderSavedBookmarksView();
            }
            else
            {
                ExecuteDictionarySearchNow();
            }
        }

        private int _dictAsyncSearchVersion;

        private bool ShouldRunDictionaryLookupAsync()
        {
            return IsHandleCreated &&
                   !IsDisposed &&
                   RequestIsDictionaryIndexReady != null &&
                   !RequestIsDictionaryIndexReady.Invoke();
        }

        private void RenderSavedBookmarksView()
        {
            int ver = Interlocked.Increment(ref _dictAsyncSearchVersion);
            UpdateDictionaryNavButtons();
            var savedWords = DictionaryBookmarkStore.Shared.GetBookmarkedWords();
            if (savedWords.Count > 0 && ShouldRunDictionaryLookupAsync())
            {
                _lblDictSummary.Text = UiLocalizer.Pick(
                    "⏳ Preparing saved bookmarks...",
                    "⏳ မှတ်သားထားသော ဝေါဟာရများ ပြင်ဆင်နေပါသည်...");
                Task.Run(() =>
                {
                    var asyncEntries = RequestGetBookmarkedEntries?.Invoke(savedWords) ?? new List<DictionaryEntry>();
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (IsDisposed || _activeTab != TaskPaneWorkspaceTab.Dictionary || !_showingSavedBookmarksView || ver != Volatile.Read(ref _dictAsyncSearchVersion))
                                    return;
                                RenderSavedBookmarksEntries(asyncEntries);
                            }));
                        }
                    }
                    catch { }
                });
                return;
            }

            var entries = savedWords.Count > 0
                ? (RequestGetBookmarkedEntries?.Invoke(savedWords) ?? new List<DictionaryEntry>())
                : new List<DictionaryEntry>();
            RenderSavedBookmarksEntries(entries);
        }

        private void RenderSavedBookmarksEntries(List<DictionaryEntry> entries)
        {
            if (entries.Count == 0)
            {
                _cardsPanel.SuspendLayout();
                DisposeAndClearCardsPanel();

                int cardWidth = GetTargetCardWidth();
                int innerWrap = Math.Max(180, cardWidth - 32);

                _lblDictSummary.Text = UiLocalizer.Pick(
                    "★ Saved Dictionary Bookmarks (0) — synced across Word & Excel",
                    "★ မှတ်သားထားသော အဘိဓာန် ဝေါဟာရများ (0) — Word နှင့် Excel ချိတ်ဆက်ထားသည်");

                var empty = CreateStyledCardContainer(cardWidth, Color.FromArgb(196, 118, 12));
                empty.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                empty.Controls.Add(new Label
                {
                    Text = UiLocalizer.Pick(
                        "No saved dictionary bookmarks yet.\n• Click \"☆ Save\" on any Myanmar or English dictionary card to bookmark important words across both Excel and Word.",
                        "မှတ်သားထားသော အဘိဓာန် ဝေါဟာရ မရှိသေးပါ။\n• မည်သည့် မြန်မာ သို့မဟုတ် အင်္ဂလိပ် အဘိဓာန်ကတ်တွင်မဆို \"☆ မှတ်မည်\" ကိုနှိပ်၍ Excel နှင့် Word နှစ်မျိုးလုံး၌ ပြန်လည်ကြည့်ရှုနိုင်ပါသည်။"),
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(70, 78, 92),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag
                }, 0, 0);
                _cardsPanel.Controls.Add(empty);
                _cardsPanel.ResumeLayout(true);
                return;
            }

            RenderDictionaryResults(
                UiLocalizer.Pick($"★ Saved Bookmarks ({entries.Count})", $"★ မှတ်သားထားသော ဝေါဟာရများ ({entries.Count})"),
                entries,
                0.0,
                InitialDictionaryRenderLimit,
                convertedFromZawgyi: false,
                effectiveQuery: string.Empty,
                isCustomHeaderTitle: true);
        }

        private void SetDictionaryCategory(DictionaryCategoryFilter filter)
        {
            _showingSavedBookmarksView = false;
            _dictCategoryFilter = filter;
            UpdateDictionaryCategoryStyles();
            UpdateDictionaryNavButtons();

            if (filter == DictionaryCategoryFilter.EncyclopediaKnowledge && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                int ver = Interlocked.Increment(ref _dictAsyncSearchVersion);
                if (ShouldRunDictionaryLookupAsync())
                {
                    _lblDictSummary.Text = UiLocalizer.Pick(
                        "⏳ Preparing offline dictionary & encyclopedia index (first lookup only)...",
                        "⏳ အဘိဓာန်နှင့် စွယ်စုံကျမ်း အညွှန်းစနစ် ပြင်ဆင်နေပါသည် (ပထမအကြိမ်သာ)...");
                    Task.Run(() =>
                    {
                        var sw = Stopwatch.StartNew();
                        var asyncFeatured = RequestGetFeaturedKnowledge?.Invoke() ?? new List<DictionaryEntry>();
                        sw.Stop();
                        try
                        {
                            if (!IsDisposed && IsHandleCreated)
                            {
                                BeginInvoke((Action)(() =>
                                {
                                    if (IsDisposed || _activeTab != TaskPaneWorkspaceTab.Dictionary || ver != Volatile.Read(ref _dictAsyncSearchVersion))
                                        return;
                                    if (asyncFeatured.Count > 0)
                                    {
                                        RenderDictionaryResults(
                                            UiLocalizer.Pick("Encyclopedia (Featured)", "စွယ်စုံဗဟုသုတ (လေ့လာဖွယ်)"),
                                            asyncFeatured,
                                            sw.Elapsed.TotalMilliseconds,
                                            InitialDictionaryRenderLimit,
                                            convertedFromZawgyi: false,
                                            effectiveQuery: string.Empty,
                                            isCustomHeaderTitle: true);
                                    }
                                    else
                                    {
                                        ExecuteDictionarySearchNow();
                                    }
                                }));
                            }
                        }
                        catch { }
                    });
                    return;
                }

                var syncSw = Stopwatch.StartNew();
                var featured = RequestGetFeaturedKnowledge?.Invoke() ?? new List<DictionaryEntry>();
                syncSw.Stop();
                if (featured.Count > 0)
                {
                    RenderDictionaryResults(
                        UiLocalizer.Pick("Encyclopedia (Featured)", "စွယ်စုံဗဟုသုတ (လေ့လာဖွယ်)"),
                        featured,
                        syncSw.Elapsed.TotalMilliseconds,
                        InitialDictionaryRenderLimit,
                        convertedFromZawgyi: false,
                        effectiveQuery: string.Empty,
                        isCustomHeaderTitle: true);
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

        private (List<DictionaryEntry> Filtered, double ElapsedMs) ExecuteExcelDictionaryQueryCore(
            string query,
            string effectiveQuery,
            DictionaryCategoryFilter categoryFilter)
        {
            var sw = Stopwatch.StartNew();
            List<DictionaryEntry> filtered;
            if (categoryFilter == DictionaryCategoryFilter.EncyclopediaKnowledge && RequestSearchReferenceCorpus != null)
            {
                filtered = RequestSearchReferenceCorpus.Invoke(effectiveQuery, false, true);
            }
            else if (categoryFilter == DictionaryCategoryFilter.ProverbsAndSayings && RequestSearchReferenceCorpus != null)
            {
                filtered = RequestSearchReferenceCorpus.Invoke(effectiveQuery, true, false);
            }
            else
            {
                var raw = RequestDictionarySearch?.Invoke(query) ?? new List<DictionaryEntry>();
                filtered = categoryFilter switch
                {
                    DictionaryCategoryFilter.OfficialThatpon => raw.Where(e => !e.IsReferenceEntry && e.IsOfficialThatpon).ToList(),
                    DictionaryCategoryFilter.AbidanMeaning => raw.Where(e =>
                        e.IsBilingualLexiconEntry ||
                        (!e.IsReferenceEntry && (!string.IsNullOrWhiteSpace(e.MeaningCue) || e.HasAbidanMetadata))).ToList(),
                    DictionaryCategoryFilter.EncyclopediaKnowledge => raw.Where(e => e.IsEncyclopediaEntry).ToList(),
                    DictionaryCategoryFilter.SpecializedDomains => raw.Where(e => e.IsSpecializedDomain).ToList(),
                    DictionaryCategoryFilter.ProperNounsAndPlaces => raw.Where(e => e.IsProperNoun).ToList(),
                    DictionaryCategoryFilter.ProverbsAndSayings => raw.Where(e => e.IsProverbOrSayingEntry).ToList(),
                    _ => raw
                };
            }
            sw.Stop();
            return (filtered, sw.Elapsed.TotalMilliseconds);
        }

        private void ExecuteDictionarySearchNow()
        {
            if (_activeTab != TaskPaneWorkspaceTab.Dictionary)
                return;

            int ver = Interlocked.Increment(ref _dictAsyncSearchVersion);
            _showingSavedBookmarksView = false;
            string query = _txtDictionarySearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                UpdateDictionaryNavButtons();
                RenderDictionaryDiscoveryCards();
                return;
            }

            if (!_suppressDictHistoryPush)
            {
                PushDictionaryHistory(query);
            }
            UpdateDictionaryNavButtons();

            string effectiveQuery = MyanmarTrie.NormalizeSearchQuery(query, out bool convertedFromZawgyi);
            DictionaryCategoryFilter categorySnapshot = _dictCategoryFilter;

            if (ShouldRunDictionaryLookupAsync())
            {
                _lblDictSummary.Text = UiLocalizer.Pick(
                    "⏳ Preparing offline dictionary index (first lookup only)...",
                    "⏳ အဘိဓာန် အညွှန်းစနစ် ပြင်ဆင်နေပါသည် (ပထမအကြိမ်သာ)...");
                Task.Run(() =>
                {
                    var (asyncFiltered, asyncMs) = ExecuteExcelDictionaryQueryCore(query, effectiveQuery, categorySnapshot);
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (IsDisposed || _activeTab != TaskPaneWorkspaceTab.Dictionary || _showingSavedBookmarksView || ver != Volatile.Read(ref _dictAsyncSearchVersion))
                                    return;
                                RenderDictionaryResults(
                                    query,
                                    asyncFiltered,
                                    asyncMs,
                                    InitialDictionaryRenderLimit,
                                    convertedFromZawgyi,
                                    effectiveQuery,
                                    isCustomHeaderTitle: false);
                            }));
                        }
                    }
                    catch { }
                });
                return;
            }

            var (filtered, elapsedMs) = ExecuteExcelDictionaryQueryCore(query, effectiveQuery, categorySnapshot);
            RenderDictionaryResults(
                query,
                filtered,
                elapsedMs,
                InitialDictionaryRenderLimit,
                convertedFromZawgyi,
                effectiveQuery,
                isCustomHeaderTitle: false);
        }

        private void RenderDictionaryDiscoveryCards()
        {
            int ver = Interlocked.Increment(ref _dictAsyncSearchVersion);
            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            _lblDictSummary.Text = UiLocalizer.Pick(
                "Bilingual EN ⇄ MM (57,000+), Official Thatpon (95,000+), Encyclopedia (25,000+) & Romanized Search",
                "အင်္ဂလိပ် ⇄ မြန်မာ (၅၇,၀၀၀+)၊ သတ်ပုံကျမ်း (၉၅,၀၀၀+)၊ စွယ်စုံကျမ်း (၂၅,၀၀၀+) နှင့် အသံထွက်ရှာဖွေမှု");

            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188));
            int r = 0;

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(new Label
            {
                Text = UiLocalizer.Pick(
                    "Bilingual EN ⇄ MM Dictionary, Phonetics & Encyclopedia (Excel Offline)",
                    "အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန်ကြီး၊ အသံထွက် နှင့် စွယ်စုံဗဟုသုတကျမ်း (Excel Offline)"),
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
                    "• Select any Myanmar or English cell and open Dictionary to look it up automatically, or type Romanized Burmese (\"mingalaba\", \"thatpon\") or Zawgyi.\n• Includes dual pronunciation + offline English audio (▶ EN), numbered senses, collocations (▸), and shared ★ Saved bookmarks —",
                    "• Excel အကွက်ကိုရွေး၍ အဘိဓာန်ဖွင့်ပါက အလိုအလျောက် ရှာပေးသည့်အပြင် English, Romanized (\"mingalaba\") သို့မဟုတ် ဇော်ဂျီဖြင့်လည်း ရှာဖွေနိုင်ပါသည်။\n• အသံထွက် + အင်္ဂလိပ် အသံထွက် (▶ EN)၊ တွဲဖက်သုံးဝေါဟာရ (▸) နှင့် ★ မှတ်သားစနစ်များ ပါဝင်ပါသည် —"),
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

            string[] samples = { "ဘဏ္ဍာရေး", "abandon", "mingalaba", "negotiate", "စာရင်းအင်း", "မြန်မာသဒ္ဒါ", "ကျောက်ဆည်" };
            foreach (string s in samples)
            {
                string captured = s;
                var chip = CreatePillButton($"▸ {captured}", Color.FromArgb(238, 244, 255), Color.FromArgb(22, 74, 158), Color.FromArgb(196, 214, 245), _fontBrandStats);
                chip.Click += (sender, e) => OpenDictionaryWithQuery(captured);
                sampleFlow.Controls.Add(chip);
            }

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(sampleFlow, 0, r++);
            _cardsPanel.Controls.Add(card);

            if (ShouldRunDictionaryLookupAsync())
            {
                _cardsPanel.ResumeLayout(true);
                Task.Run(() =>
                {
                    var asyncFeatured = RequestGetFeaturedKnowledge?.Invoke() ?? new List<DictionaryEntry>();
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (IsDisposed ||
                                    _activeTab != TaskPaneWorkspaceTab.Dictionary ||
                                    _showingSavedBookmarksView ||
                                    !string.IsNullOrWhiteSpace(_txtDictionarySearch.Text) ||
                                    ver != Volatile.Read(ref _dictAsyncSearchVersion))
                                {
                                    return;
                                }

                                _cardsPanel.SuspendLayout();
                                int cw = GetTargetCardWidth();
                                int iw = Math.Max(180, cw - 32);
                                foreach (var entry in asyncFeatured.Take(5))
                                {
                                    _cardsPanel.Controls.Add(BuildDictionaryEntryCard(entry, cw, iw));
                                }
                                _cardsPanel.ResumeLayout(true);
                            }));
                        }
                    }
                    catch { }
                });
                return;
            }

            var featured = RequestGetFeaturedKnowledge?.Invoke() ?? new List<DictionaryEntry>();
            foreach (var entry in featured.Take(5))
            {
                _cardsPanel.Controls.Add(BuildDictionaryEntryCard(entry, cardWidth, innerWrap));
            }

            _cardsPanel.ResumeLayout(true);
        }

        private const int InitialDictionaryRenderLimit = 15;

        private void RenderDictionaryResults(
            string query,
            List<DictionaryEntry> entries,
            double elapsedMs,
            int maxRender = InitialDictionaryRenderLimit,
            bool convertedFromZawgyi = false,
            string effectiveQuery = "",
            bool isCustomHeaderTitle = false)
        {
            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            string speedSuffix = elapsedMs > 0.0 ? $" ({elapsedMs:0.0} ms)" : string.Empty;
            string zawgyiSuffix = convertedFromZawgyi && !string.IsNullOrEmpty(effectiveQuery)
                ? $"  [Zawgyi → {effectiveQuery}]"
                : string.Empty;

            if (isCustomHeaderTitle)
            {
                _lblDictSummary.Text = $"{query}{speedSuffix}";
            }
            else
            {
                _lblDictSummary.Text = entries.Count > 0
                    ? UiLocalizer.Pick(
                        $"Found {entries.Count} result(s) for \"{query}\"{zawgyiSuffix}{speedSuffix}",
                        $"\"{query}\" အတွက် ရလဒ် ({entries.Count}) ပုဒ် တွေ့ရှိသည်{zawgyiSuffix}{speedSuffix}")
                    : UiLocalizer.Pick(
                        $"No dictionary entries matched \"{query}\"{zawgyiSuffix}",
                        $"\"{query}\" နှင့် ကိုက်ညီသော အဘိဓာန်ရလဒ် မတွေ့ရှိပါ{zawgyiSuffix}");
            }

            if (entries.Count == 0)
            {
                var empty = CreateStyledCardContainer(cardWidth, Color.FromArgb(180, 120, 24));
                empty.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                empty.Controls.Add(new Label
                {
                    Text = UiLocalizer.Pick(
                        $"No match for \"{query}\" — try searching an English word, Myanmar root syllable, or Romanized spelling (e.g., \"mingalaba\").",
                        $"\"{query}\" ကို မတွေ့ရှိပါ — အင်္ဂလိပ်စာလုံး၊ မြန်မာ အရင်းခံစကားလုံး သို့မဟုတ် Romanized အသံထွက် (ဥပမာ \"mingalaba\") ဖြင့် ပြန်လည်ရှာဖွေကြည့်ပါ။"),
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
                moreBtn.Click += (s, e) => RenderDictionaryResults(
                    query,
                    entries,
                    elapsedMs,
                    takeCount + 30,
                    convertedFromZawgyi,
                    effectiveQuery,
                    isCustomHeaderTitle);
                _cardsPanel.Controls.Add(moreBtn);
            }

            _cardsPanel.ResumeLayout(true);
        }

        private Control BuildDictionaryEntryCard(DictionaryEntry entry, int cardWidth, int innerWrap)
        {
            bool isBilingual = entry.IsBilingualLexiconEntry;
            Color stripe = isBilingual
                ? Color.FromArgb(20, 88, 168)
                : (entry.IsEncyclopediaEntry
                    ? Color.FromArgb(124, 58, 202)
                    : (entry.IsProverbOrSayingEntry ? Color.FromArgb(196, 118, 12) : Color.FromArgb(24, 90, 188)));

            var card = CreateStyledCardContainer(cardWidth, stripe);
            int r = 0;

            var topRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 3)
            };
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            topRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblHead = new Label
            {
                Text = entry.Word,
                Font = _fontCardWord,
                ForeColor = Color.FromArgb(18, 44, 96),
                AutoSize = true,
                MaximumSize = new Size(Math.Max(120, innerWrap - 95), 0),
                Dock = DockStyle.Fill
            };

            string posText = isBilingual && !string.IsNullOrWhiteSpace(entry.EntityCategory)
                ? $"{entry.LocalizedPosDisplayLabel} ({entry.EntityCategory})"
                : entry.LocalizedPosDisplayLabel;
            var lblPos = new Label
            {
                Text = posText,
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

            // Compact Dual Pronunciation Line + Offline English TTS Audio Button ([▶ EN])
            var pronInfo = entry.IsReferenceEntry
                ? default
                : MyanmarPhoneticTranscriber.GetPronunciation(entry.Word, entry.BurmesePronunciation);
            string speakEn = entry.GetSpeakableEnglishText();
            if (pronInfo.HasValue || !string.IsNullOrEmpty(speakEn))
            {
                var pronRow = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    RowCount = 1,
                    BackColor = Color.FromArgb(244, 247, 253),
                    Padding = new Padding(6, 3, 6, 3),
                    Margin = new Padding(0, 0, 0, 4)
                };
                pronRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                pronRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                pronRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                string pronDisplay = pronInfo.HasValue
                    ? MyanmarPhoneticTranscriber.FormatCompactDualLine(pronInfo, UiLocalizer.IsEnglish)
                    : UiLocalizer.Pick($"English Audio: {speakEn}", $"အင်္ဂလိပ် အသံထွက်: {speakEn}");
                var lblPron = new Label
                {
                    Text = pronDisplay,
                    Font = _fontBrandStats,
                    ForeColor = Color.FromArgb(38, 56, 88),
                    AutoSize = true,
                    MaximumSize = new Size(Math.Max(120, innerWrap - (!string.IsNullOrEmpty(speakEn) ? 80 : 16)), 0),
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 2, 4, 2)
                };
                if (pronInfo.HasValue && !string.IsNullOrWhiteSpace(pronInfo.Ipa))
                {
                    _toolTip.SetToolTip(lblPron, MyanmarPhoneticTranscriber.FormatIpaTooltip(pronInfo));
                }
                pronRow.Controls.Add(lblPron, 0, 0);

                if (!string.IsNullOrEmpty(speakEn))
                {
                    string capturedSpeakEn = speakEn;
                    var btnSpeakEn = CreatePillButton(
                        UiLocalizer.Pick("▶ EN", "▶ အသံ"),
                        Color.FromArgb(228, 241, 255),
                        Color.FromArgb(16, 72, 158),
                        Color.FromArgb(174, 204, 246),
                        _fontSmallBold);
                    btnSpeakEn.Dock = DockStyle.None;
                    btnSpeakEn.Anchor = AnchorStyles.Right;
                    btnSpeakEn.Margin = new Padding(2, 0, 0, 0);
                    _toolTip.SetToolTip(
                        btnSpeakEn,
                        UiLocalizer.Pick(
                            $"Speak English pronunciation offline: \"{capturedSpeakEn}\"",
                            $"\"{capturedSpeakEn}\" ၏ အင်္ဂလိပ်အသံထွက် နားထောင်ရန် (Offline)"));
                    btnSpeakEn.Click += (s, e) => OfflineSpeechService.SpeakEnglishAsync(capturedSpeakEn);
                    pronRow.Controls.Add(btnSpeakEn, 1, 0);
                }

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(pronRow, 0, r++);
            }

            if (!isBilingual && !string.IsNullOrWhiteSpace(entry.EnglishName))
            {
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(new Label
                {
                    Text = $"EN: {entry.EnglishName}",
                    Font = _fontSmallBold,
                    ForeColor = Color.FromArgb(16, 68, 148),
                    BackColor = Color.FromArgb(235, 245, 255),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Padding = new Padding(6, 3, 6, 3),
                    Margin = new Padding(0, 0, 0, 4)
                }, 0, r++);
            }

            var formattedSenses = entry.GetFormattedSenses();
            if (formattedSenses.Count > 0)
            {
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(new Label
                {
                    Text = string.Join("\n", formattedSenses),
                    Font = _fontBody,
                    ForeColor = Color.FromArgb(30, 40, 56),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Margin = new Padding(0, 0, 0, 4)
                }, 0, r++);
            }

            if (!isBilingual && !string.IsNullOrWhiteSpace(entry.FormattedUsageDetails))
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

            List<string> syns;
            if (isBilingual && !string.IsNullOrWhiteSpace(entry.Synonyms))
            {
                syns = entry.Synonyms
                    .Split(new[] { '၊', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length >= 1)
                    .Distinct(StringComparer.Ordinal)
                    .Take(6)
                    .ToList();
            }
            else
            {
                syns = !entry.IsReferenceEntry
                    ? (RequestGetSynonyms?.Invoke(entry.Word) ?? new List<string>())
                    : new List<string>();
            }

            var ants = !entry.IsReferenceEntry
                ? (RequestGetAntonyms?.Invoke(entry.Word) ?? new List<string>())
                : new List<string>();

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
                foreach (string syn in syns.Take(6))
                {
                    string cap = syn;
                    var chip = CreatePillButton($"⇄ {cap}", Color.FromArgb(236, 246, 240), Color.FromArgb(16, 102, 52), Color.FromArgb(186, 222, 198), _fontBrandStats);
                    chip.Click += (s, e) => OpenDictionaryWithQuery(cap);
                    relFlow.Controls.Add(chip);
                }
                foreach (string ant in ants.Take(4))
                {
                    string cap = ant;
                    var chip = CreatePillButton($"≠ {cap}", Color.FromArgb(254, 240, 240), Color.FromArgb(168, 32, 32), Color.FromArgb(236, 198, 198), _fontBrandStats);
                    chip.Click += (s, e) => OpenDictionaryWithQuery(cap);
                    relFlow.Controls.Add(chip);
                }
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(relFlow, 0, r++);
            }

            if (!entry.IsReferenceEntry)
            {
                var collocations = RequestGetCollocations?.Invoke(entry.Word) ?? new List<string>();
                if (collocations.Count > 0)
                {
                    var collocFlow = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Top,
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        WrapContents = true,
                        Margin = new Padding(0, 0, 0, 4)
                    };
                    foreach (string colloc in collocations.Take(5))
                    {
                        string cap = colloc;
                        var chip = CreatePillButton($"▸ {cap}", Color.FromArgb(244, 242, 255), Color.FromArgb(66, 38, 138), Color.FromArgb(208, 198, 244), _fontBrandStats);
                        _toolTip.SetToolTip(
                            chip,
                            UiLocalizer.Pick(
                                $"Common collocation — click to look up \"{cap}\"",
                                $"အသုံးများသော တွဲဖက်ဝေါဟာရ — \"{cap}\" ကို နှိပ်၍ ရှာဖွေပါ"));
                        chip.Click += (s, e) => OpenDictionaryWithQuery(cap);
                        collocFlow.Controls.Add(chip);
                    }
                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(collocFlow, 0, r++);
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
            bool initialSaved = DictionaryBookmarkStore.Shared.IsBookmarked(wordToUse);
            var btnSave = CreatePillButton(
                initialSaved
                    ? UiLocalizer.Pick("★ Saved", "★ မှတ်ပြီး")
                    : UiLocalizer.Pick("☆ Save", "☆ မှတ်မည်"),
                initialSaved ? Color.FromArgb(255, 246, 218) : Color.FromArgb(245, 247, 250),
                initialSaved ? Color.FromArgb(152, 86, 6) : Color.FromArgb(44, 56, 74),
                initialSaved ? Color.FromArgb(238, 204, 128) : Color.FromArgb(212, 220, 232),
                initialSaved ? _fontSmallBold : _fontBrandStats);
            btnSave.Click += (s, e) =>
            {
                bool nowSaved = DictionaryBookmarkStore.Shared.ToggleBookmark(wordToUse);
                btnSave.Text = nowSaved
                    ? UiLocalizer.Pick("★ Saved", "★ မှတ်ပြီး")
                    : UiLocalizer.Pick("☆ Save", "☆ မှတ်မည်");
                btnSave.BackColor = nowSaved ? Color.FromArgb(255, 246, 218) : Color.FromArgb(245, 247, 250);
                btnSave.ForeColor = nowSaved ? Color.FromArgb(152, 86, 6) : Color.FromArgb(44, 56, 74);
                btnSave.FlatAppearance.BorderColor = nowSaved ? Color.FromArgb(238, 204, 128) : Color.FromArgb(212, 220, 232);
                btnSave.Font = nowSaved ? _fontSmallBold : _fontBrandStats;
                UpdateDictionaryNavButtons();
            };

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

            btnRow.Controls.Add(btnSave);
            btnRow.Controls.Add(btnCopy);
            btnRow.Controls.Add(btnInsert);

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnRow, 0, r++);

            return card;
        }
    }
}
