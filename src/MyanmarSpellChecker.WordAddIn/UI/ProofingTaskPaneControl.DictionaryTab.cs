// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

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

namespace MyanmarSpellChecker.WordAddIn.UI
{
    public sealed partial class ProofingTaskPaneControl
    {
        // ================= DICTIONARY & PROVERBS EXPLORER (ZERO-UI-FREEZE PIPELINE) =================

        private const int InitialDictionaryRenderLimit = 12;
        private const int DictionaryAppendBatchSize = 20;

        private readonly DictionaryLookupCache _dictLookupCache = new DictionaryLookupCache(32);
        private int _dictAsyncSearchVersion;
        private int _dictPrewarmInFlight;

        private void TriggerBackgroundDictionaryPrewarmIfNeeded()
        {
            if (IsDisposed || RequestPrewarmDictionary == null)
                return;
            if (RequestIsEngineReady != null && !RequestIsEngineReady.Invoke())
                return;
            if (RequestIsDictionaryIndexReady != null && RequestIsDictionaryIndexReady.Invoke())
                return;

            if (Interlocked.CompareExchange(ref _dictPrewarmInFlight, 1, 0) != 0)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    RequestPrewarmDictionary?.Invoke();
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref _dictPrewarmInFlight, 0);
                }
            });
        }

        private bool IsDictionaryEngineStillWarmingUp() =>
            _isEngineWarmingUp || (RequestIsEngineReady != null && !RequestIsEngineReady.Invoke());

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
                SetDictionarySearchTextSilently(_dictHistory[_dictHistoryIndex]);
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

        private PreparedDictionaryCard[] PrepareCardsOffThread(
            IReadOnlyList<DictionaryEntry> entries,
            int startIndex,
            int count,
            bool isEnglishUi)
        {
            return DictionaryViewPreparationService.PrepareCardsSlice(
                entries,
                startIndex,
                count,
                isEnglishUi,
                maxSynonymsPerCard: 8,
                maxAntonymsPerCard: 8,
                maxCollocationsPerCard: 6,
                RequestGetSynonyms,
                RequestGetAntonyms,
                RequestGetCollocations,
                RequestHomophoneSummary,
                DictionaryBookmarkStore.Shared);
        }

        private void RenderDictionaryEngineWarmupCard(string pendingQuery)
        {
            BeginCardsBatchUpdate();
            try
            {
                DisposeAndClearCardsPanel();

                _lblDictSummary.Text = UiLocalizer.Pick(
                    "⟳ Connecting offline Myanmar dictionary engine...",
                    "⟳ Offline အဘိဓာန် အင်ဂျင်ကို စတင်ချိတ်ဆက်နေပါသည်...");
                _lblDictSummary.ForeColor = Color.FromArgb(24, 82, 172);

                int cardWidth = GetTargetCardWidth();
                int innerWrap = Math.Max(180, cardWidth - 32);
                var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188), isSelectableItem: null);
                card.SuspendLayout();
                var lblInfo = new Label
                {
                    Text = string.IsNullOrWhiteSpace(pendingQuery)
                        ? UiLocalizer.Pick(
                            "Loading offline lexicon & encyclopedia in the background — ready in a moment...",
                            "သတ်ပုံကျမ်းနှင့် စွယ်စုံအဘိဓာန်ကို နောက်ခံတွင် ဖွင့်လှစ်နေပါသည် — ခေတ္တစောင့်ဆိုင်းပေးပါ...")
                        : UiLocalizer.Pick(
                            $"Loading offline dictionary engine — \"{pendingQuery}\" will appear automatically in a moment...",
                            $"အဘိဓာန်အင်ဂျင် ဖွင့်လှစ်ပြီးသည်နှင့် \"{pendingQuery}\" ကို အလိုအလျောက် ချက်ချင်းပြသပေးပါမည်..."),
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(58, 68, 84),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblInfo, 0, 0);
                card.ResumeLayout(false);
                _cardsPanel.Controls.Add(card);
            }
            finally
            {
                EndCardsBatchUpdate();
            }
        }

        private void LoadFeaturedReferenceWordView(
            bool isProverbs,
            string titleEn,
            string titleMm,
            string? fallbackQueryOnEmpty)
        {
            _searchDebounceTimer.Stop();
            int ver = Interlocked.Increment(ref _dictAsyncSearchVersion);

            if (IsDictionaryEngineStillWarmingUp())
            {
                RenderDictionaryEngineWarmupCard(string.Empty);
                return;
            }

            bool isEn = UiLocalizer.IsEnglish;
            Func<List<DictionaryEntry>>? provider = isProverbs ? RequestGetFeaturedProverbs : RequestGetFeaturedKnowledge;

            if (IsHandleCreated && !IsDisposed)
            {
                bool indexCold = RequestIsDictionaryIndexReady != null && !RequestIsDictionaryIndexReady.Invoke();
                _lblDictSummary.Text = indexCold
                    ? UiLocalizer.Pick(
                        "⏳ Preparing offline dictionary & encyclopedia index (first lookup only)...",
                        "⏳ အဘိဓာန်နှင့် စွယ်စုံကျမ်း အညွှန်းစနစ် ပြင်ဆင်နေပါသည် (ပထမအကြိမ်သာ)...")
                    : UiLocalizer.Pick("⟳ Loading featured entries...", "⟳ လေ့လာဖွယ် ဝေါဟာရများ ပြင်ဆင်နေပါသည်...");
                _lblDictSummary.ForeColor = Color.FromArgb(24, 82, 172);

                _ = Task.Run(() =>
                {
                    if (ver != Volatile.Read(ref _dictAsyncSearchVersion))
                        return;

                    var sw = Stopwatch.StartNew();
                    var featured = provider?.Invoke() ?? new List<DictionaryEntry>();
                    var prepared = PrepareCardsOffThread(featured, 0, InitialDictionaryRenderLimit, isEn);
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;

                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (IsDisposed || _activeTab != TaskPaneWorkspaceTab.Dictionary || ver != Volatile.Read(ref _dictAsyncSearchVersion))
                                    return;
                                if (featured.Count > 0)
                                {
                                    RenderPreparedDictionaryResults(
                                        UiLocalizer.Pick(titleEn, titleMm),
                                        featured,
                                        prepared,
                                        ms,
                                        convertedFromZawgyi: false,
                                        effectiveQuery: string.Empty,
                                        isCustomHeaderTitle: true,
                                        renderVersion: ver);
                                }
                                else if (!string.IsNullOrWhiteSpace(fallbackQueryOnEmpty))
                                {
                                    SetDictionarySearchTextSilently(fallbackQueryOnEmpty);
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
            var syncFeatured = provider?.Invoke() ?? new List<DictionaryEntry>();
            var syncPrepared = PrepareCardsOffThread(syncFeatured, 0, InitialDictionaryRenderLimit, isEn);
            syncSw.Stop();
            if (syncFeatured.Count > 0)
            {
                RenderPreparedDictionaryResults(
                    UiLocalizer.Pick(titleEn, titleMm),
                    syncFeatured,
                    syncPrepared,
                    syncSw.Elapsed.TotalMilliseconds,
                    convertedFromZawgyi: false,
                    effectiveQuery: string.Empty,
                    isCustomHeaderTitle: true,
                    renderVersion: ver);
            }
            else if (!string.IsNullOrWhiteSpace(fallbackQueryOnEmpty))
            {
                SetDictionarySearchTextSilently(fallbackQueryOnEmpty);
                ExecuteDictionarySearchNow();
            }
        }

        private void RenderSavedBookmarksView()
        {
            _searchDebounceTimer.Stop();
            int ver = Interlocked.Increment(ref _dictAsyncSearchVersion);
            UpdateDictionaryNavButtons();

            if (IsDictionaryEngineStillWarmingUp())
            {
                RenderDictionaryEngineWarmupCard(string.Empty);
                return;
            }

            var savedWords = DictionaryBookmarkStore.Shared.GetBookmarkedWords();
            if (savedWords.Count == 0)
            {
                RenderSavedBookmarksEntries(new List<DictionaryEntry>(), Array.Empty<PreparedDictionaryCard>(), ver);
                return;
            }

            bool isEn = UiLocalizer.IsEnglish;
            if (IsHandleCreated && !IsDisposed)
            {
                _lblDictSummary.Text = UiLocalizer.Pick(
                    "⏳ Loading saved bookmarks...",
                    "⏳ မှတ်သားထားသော ဝေါဟာရများ ဖွင့်လှစ်နေပါသည်...");
                _lblDictSummary.ForeColor = Color.FromArgb(148, 86, 8);

                _ = Task.Run(() =>
                {
                    if (ver != Volatile.Read(ref _dictAsyncSearchVersion))
                        return;

                    var asyncEntries = RequestGetBookmarkedEntries?.Invoke(savedWords) ?? new List<DictionaryEntry>();
                    var prepared = PrepareCardsOffThread(asyncEntries, 0, InitialDictionaryRenderLimit, isEn);
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (IsDisposed || _activeTab != TaskPaneWorkspaceTab.Dictionary || !_showingSavedBookmarksView || ver != Volatile.Read(ref _dictAsyncSearchVersion))
                                    return;
                                RenderSavedBookmarksEntries(asyncEntries, prepared, ver);
                            }));
                        }
                    }
                    catch { }
                });
                return;
            }

            var entries = RequestGetBookmarkedEntries?.Invoke(savedWords) ?? new List<DictionaryEntry>();
            var syncPrepared = PrepareCardsOffThread(entries, 0, InitialDictionaryRenderLimit, isEn);
            RenderSavedBookmarksEntries(entries, syncPrepared, ver);
        }

        private void RenderSavedBookmarksEntries(
            List<DictionaryEntry> entries,
            PreparedDictionaryCard[] initialPrepared,
            int renderVersion)
        {
            if (entries.Count == 0)
            {
                BeginCardsBatchUpdate();
                try
                {
                    DisposeAndClearCardsPanel();

                    _lblDictSummary.Text = UiLocalizer.Pick(
                        "★ Saved Dictionary Bookmarks (0) — synced across Word & Excel",
                        "★ မှတ်သားထားသော အဘိဓာန် ဝေါဟာရများ (0) — Word နှင့် Excel ချိတ်ဆက်ထားသည်");
                    _lblDictSummary.ForeColor = Color.FromArgb(148, 86, 8);

                    int cardWidth = GetTargetCardWidth();
                    int innerWrap = Math.Max(180, cardWidth - 32);
                    var emptyCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(196, 118, 12), isSelectableItem: null);
                    emptyCard.SuspendLayout();
                    var lblEmpty = new Label
                    {
                        Text = UiLocalizer.Pick(
                            "No saved dictionary bookmarks yet.\n• Click \"☆ Save\" on any Myanmar or English dictionary card to bookmark important words for instant offline review in both Word and Excel.",
                            "မှတ်သားထားသော အဘိဓာန် ဝေါဟာရ မရှိသေးပါ။\n• မည်သည့် မြန်မာ သို့မဟုတ် အင်္ဂလိပ် အဘိဓာန်ကတ်တွင်မဆို \"☆ မှတ်မည်\" ကိုနှိပ်၍ Word နှင့် Excel နှစ်မျိုးလုံး၌ အချိန်မရွေး ပြန်လည်ကြည့်ရှုနိုင်ပါသည်။"),
                        Font = _fontSmall,
                        ForeColor = Color.FromArgb(70, 78, 92),
                        AutoSize = true,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill
                    };
                    emptyCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    emptyCard.Controls.Add(lblEmpty, 0, 0);
                    emptyCard.ResumeLayout(false);
                    _cardsPanel.Controls.Add(emptyCard);
                }
                finally
                {
                    EndCardsBatchUpdate();
                }
                return;
            }

            RenderPreparedDictionaryResults(
                UiLocalizer.Pick($"★ Saved Bookmarks ({entries.Count})", $"★ မှတ်သားထားသော ဝေါဟာရများ ({entries.Count})"),
                entries,
                initialPrepared,
                0.0,
                convertedFromZawgyi: false,
                effectiveQuery: string.Empty,
                isCustomHeaderTitle: true,
                renderVersion: renderVersion);
        }

        private void SetDictionaryCategory(DictionaryCategoryFilter filter)
        {
            _showingSavedBookmarksView = false;
            _dictCategoryFilter = filter;
            UpdateDictionaryCategoryStyles();
            UpdateDictionaryNavButtons();

            if (filter == DictionaryCategoryFilter.EncyclopediaKnowledge && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                LoadFeaturedReferenceWordView(
                    isProverbs: false,
                    "Encyclopedia (Featured Knowledge)",
                    "စွယ်စုံဗဟုသုတ (ကျပန်းလေ့လာဖွယ်)",
                    fallbackQueryOnEmpty: "မြန်မာသဒ္ဒါ");
                return;
            }
            if (filter == DictionaryCategoryFilter.ProverbsAndSayings && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                LoadFeaturedReferenceWordView(
                    isProverbs: true,
                    "Myanmar Proverbs & Sayings (Featured)",
                    "မြန်မာစကားပုံနှင့် ဆိုရိုးစကားများ (လေ့လာဖွယ်)",
                    fallbackQueryOnEmpty: "စကားပုံ");
                return;
            }
            if (filter == DictionaryCategoryFilter.SpecializedDomains && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                SetDictionarySearchTextSilently("ဥပဒေ");
                ExecuteDictionarySearchNow();
                return;
            }
            if (filter == DictionaryCategoryFilter.ProperNounsAndPlaces && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                SetDictionarySearchTextSilently("နိုင်ငံ");
                ExecuteDictionarySearchNow();
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

        private (List<DictionaryEntry> Filtered, double ElapsedMs) ExecuteDictionaryQueryCore(
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
                List<DictionaryEntry> rawMatches = RequestDictionarySearch?.Invoke(query) ?? new List<DictionaryEntry>();
                filtered = rawMatches;

                switch (categoryFilter)
                {
                    case DictionaryCategoryFilter.OfficialThatpon:
                        filtered = rawMatches.Where(e => !e.IsReferenceEntry && e.IsOfficialThatpon).ToList();
                        break;
                    case DictionaryCategoryFilter.AbidanMeaning:
                        filtered = rawMatches.Where(e =>
                            e.IsBilingualLexiconEntry ||
                            (!e.IsReferenceEntry && (!string.IsNullOrWhiteSpace(e.MeaningCue) || e.HasAbidanMetadata))).ToList();
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
            return (filtered, sw.Elapsed.TotalMilliseconds);
        }

        private void ExecuteDictionarySearchNow()
        {
            _searchDebounceTimer.Stop();
            if (_activeTab != TaskPaneWorkspaceTab.Dictionary)
                return;

            int ver = Interlocked.Increment(ref _dictAsyncSearchVersion);
            _showingSavedBookmarksView = false;
            string query = _txtDictionarySearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                UpdateDictionaryNavButtons();
                RenderDictionaryDiscoveryView();
                return;
            }

            if (!_suppressDictHistoryPush)
            {
                PushDictionaryHistory(query);
            }
            UpdateDictionaryNavButtons();

            if (IsDictionaryEngineStillWarmingUp())
            {
                RenderDictionaryEngineWarmupCard(query);
                return;
            }

            DictionaryCategoryFilter categorySnapshot = _dictCategoryFilter;
            bool isEn = UiLocalizer.IsEnglish;
            long mutVer = RequestGetEngineMutationVersion?.Invoke() ?? 0L;

            // Instant 0.0 ms LRU cache hit for Back/Next navigation, repeated chips, and backspace re-queries!
            if (_dictLookupCache.TryGet(mutVer, isEn, (int)categorySnapshot, query, out var cachedView) && cachedView != null)
            {
                RenderPreparedDictionaryResults(
                    cachedView.RawQuery,
                    cachedView.AllMatches,
                    cachedView.InitialCards,
                    0.1,
                    cachedView.ConvertedFromZawgyi,
                    cachedView.EffectiveQuery,
                    isCustomHeaderTitle: false,
                    renderVersion: ver);
                return;
            }

            if (IsHandleCreated && !IsDisposed)
            {
                bool indexCold = RequestIsDictionaryIndexReady != null && !RequestIsDictionaryIndexReady.Invoke();
                _lblDictSummary.Text = indexCold
                    ? UiLocalizer.Pick(
                        "⏳ Preparing offline dictionary index (first lookup only)...",
                        "⏳ အဘိဓာန် အညွှန်းစနစ် ပြင်ဆင်နေပါသည် (ပထမအကြိမ်သာ)...")
                    : UiLocalizer.Pick(
                        $"⟳ Searching \"{query}\"...",
                        $"⟳ \"{query}\" ကို ရှာဖွေနေပါသည်...");
                _lblDictSummary.ForeColor = Color.FromArgb(24, 82, 172);

                _ = Task.Run(() =>
                {
                    if (ver != Volatile.Read(ref _dictAsyncSearchVersion))
                        return;

                    string effectiveQuery = MyanmarTrie.NormalizeSearchQuery(query, out bool convertedFromZawgyi);
                    var (asyncFiltered, asyncMs) = ExecuteDictionaryQueryCore(query, effectiveQuery, categorySnapshot);

                    if (ver != Volatile.Read(ref _dictAsyncSearchVersion))
                        return;

                    var preparedInitial = PrepareCardsOffThread(asyncFiltered, 0, InitialDictionaryRenderLimit, isEn);
                    long currentMutVer = RequestGetEngineMutationVersion?.Invoke() ?? mutVer;
                    var viewToCache = new CachedDictionaryQueryView(
                        query,
                        effectiveQuery,
                        convertedFromZawgyi,
                        asyncMs,
                        asyncFiltered,
                        preparedInitial);
                    _dictLookupCache.Store(currentMutVer, isEn, (int)categorySnapshot, query, viewToCache);

                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (IsDisposed ||
                                    _activeTab != TaskPaneWorkspaceTab.Dictionary ||
                                    _showingSavedBookmarksView ||
                                    ver != Volatile.Read(ref _dictAsyncSearchVersion))
                                {
                                    return;
                                }

                                RenderPreparedDictionaryResults(
                                    query,
                                    asyncFiltered,
                                    preparedInitial,
                                    asyncMs,
                                    convertedFromZawgyi,
                                    effectiveQuery,
                                    isCustomHeaderTitle: false,
                                    renderVersion: ver);
                            }));
                        }
                    }
                    catch { }
                });
                return;
            }

            string syncEffective = MyanmarTrie.NormalizeSearchQuery(query, out bool syncZawgyi);
            var (syncFiltered, syncElapsedMs) = ExecuteDictionaryQueryCore(query, syncEffective, categorySnapshot);
            var syncPrepared = PrepareCardsOffThread(syncFiltered, 0, InitialDictionaryRenderLimit, isEn);
            long syncMutVer = RequestGetEngineMutationVersion?.Invoke() ?? mutVer;
            _dictLookupCache.Store(
                syncMutVer,
                isEn,
                (int)categorySnapshot,
                query,
                new CachedDictionaryQueryView(query, syncEffective, syncZawgyi, syncElapsedMs, syncFiltered, syncPrepared));

            RenderPreparedDictionaryResults(
                query,
                syncFiltered,
                syncPrepared,
                syncElapsedMs,
                syncZawgyi,
                syncEffective,
                isCustomHeaderTitle: false,
                renderVersion: ver);
        }

        private void RenderDictionaryDiscoveryView()
        {
            TriggerBackgroundDictionaryPrewarmIfNeeded();

            BeginCardsBatchUpdate();
            try
            {
                DisposeAndClearCardsPanel();

                _lblDictSummary.Text = UiLocalizer.Pick(
                    "Bilingual EN ⇄ MM (57,000+), Official Thatpon (95,000+), Encyclopedia (25,000+) & Romanized Search",
                    "အင်္ဂလိပ် ⇄ မြန်မာ (၅၇,၀၀၀+)၊ သတ်ပုံကျမ်း (၉၅,၀၀၀+)၊ စွယ်စုံကျမ်း (၂၅,၀၀၀+) နှင့် အသံထွက်ရှာဖွေမှု");
                _lblDictSummary.ForeColor = Color.FromArgb(24, 82, 172);

                int cardWidth = GetTargetCardWidth();
                int innerWrap = Math.Max(180, cardWidth - 32);

                var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188), isSelectableItem: null);
                card.SuspendLayout();
                int row = 0;

                var lblGuideTitle = new Label
                {
                    Text = UiLocalizer.Pick(
                        "Bilingual EN ⇄ MM Dictionary, Phonetics & Encyclopedia (100% Offline)",
                        "အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန်ကြီး၊ အသံထွက် နှင့် စွယ်စုံဗဟုသုတကျမ်း (100% Offline)"),
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
                    Text = UiLocalizer.Pick(
                        "• Search in English (\"abandon\", \"negotiate\"), Myanmar, Romanized Burmese (\"mingalaba\", \"thatpon\"), or pasted Zawgyi.\n• Includes dual spoken pronunciation + offline English audio (▶ EN), numbered multi-sense definitions, corpus collocations (▸), 25,000+ encyclopedia articles, and shared ★ Saved bookmarks —",
                        "• အင်္ဂလိပ်စာလုံး (\"abandon\", \"negotiate\")၊ မြန်မာစာလုံး၊ အင်္ဂလိပ်အသံထွက် (\"mingalaba\", \"thatpon\") သို့မဟုတ် ဇော်ဂျီဖောင့်ဖြင့် တိုက်ရိုက် ရှာဖွေနိုင်ပါသည်။\n• မြန်မာအသံထွက် + အင်္ဂလိပ် အသံထွက်နားထောင်ရန် (▶ EN)၊ အနက်အဓိပ္ပာယ် အမှတ်စဉ်များ၊ တွဲဖက်သုံးဝေါဟာရ (▸)၊ စွယ်စုံဗဟုသုတ (၂၅,၀၀၀+) နှင့် ★ မှတ်သားစနစ်များ ပါဝင်ပါသည် —"),
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
                    UiLocalizer.Pick(
                        "✦ Explore Random Knowledge & Lexicon",
                        "✦ ဗဟုသုတ နှင့် အဘိဓာန် ကျပန်းဖတ်ရှုရန် (Explore Knowledge)"),
                    Color.FromArgb(232, 248, 246),
                    Color.FromArgb(12, 96, 92),
                    Color.FromArgb(168, 222, 214),
                    _fontSmallBold);
                btnExploreRandom.Margin = new Padding(2, 0, 2, 7);
                btnExploreRandom.Click += (s, e) =>
                {
                    LoadFeaturedReferenceWordView(
                        isProverbs: false,
                        "Featured Encyclopedia & Dictionary Entries",
                        "ကျပန်း လေ့လာဖွယ် စွယ်စုံဗဟုသုတ နှင့် အဘိဓာန်",
                        fallbackQueryOnEmpty: null);
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(btnExploreRandom, 0, row++);

                string[] starterWords = { "သတ်ပုံ", "abandon", "mingalaba", "negotiate", "မြန်မာသဒ္ဒါ", "ကျောက်ဆည်", "ကုလသမဂ္ဂ", "ပန်းဆယ်မျိုး" };
                var chipGrid = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    RowCount = 4,
                    Margin = new Padding(0)
                };
                chipGrid.SuspendLayout();
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
                        $"▸ {sample}",
                        Color.FromArgb(240, 245, 255),
                        Color.FromArgb(22, 72, 155),
                        Color.FromArgb(196, 214, 245),
                        _fontSmall);
                    chip.Click += (s, e) =>
                    {
                        _showingSavedBookmarksView = false;
                        _dictCategoryFilter = DictionaryCategoryFilter.All;
                        UpdateDictionaryCategoryStyles();
                        SetDictionarySearchTextSilently(sample);
                        ExecuteDictionarySearchNow();
                    };
                    chipGrid.Controls.Add(chip, i % 2, i / 2);
                }
                chipGrid.ResumeLayout(false);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(chipGrid, 0, row++);
                card.ResumeLayout(false);

                _cardsPanel.Controls.Add(card);
            }
            finally
            {
                EndCardsBatchUpdate();
            }
        }

        private void RenderPreparedDictionaryResults(
            string query,
            List<DictionaryEntry> allMatches,
            PreparedDictionaryCard[] initialPreparedCards,
            double elapsedMs,
            bool convertedFromZawgyi,
            string effectiveQuery,
            bool isCustomHeaderTitle,
            int renderVersion)
        {
            BeginCardsBatchUpdate();
            try
            {
                DisposeAndClearCardsPanel();

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
                    _lblDictSummary.Text = allMatches.Count > 0
                        ? UiLocalizer.Pick(
                            $"Found {allMatches.Count} result(s) for \"{query}\"{zawgyiSuffix}{speedSuffix}",
                            $"\"{query}\" အတွက် ရလဒ် ({allMatches.Count}) ခု တွေ့ရှိသည်{zawgyiSuffix}{speedSuffix}")
                        : UiLocalizer.Pick(
                            $"No results found for \"{query}\"{zawgyiSuffix}{speedSuffix}",
                            $"\"{query}\" နှင့် ကိုက်ညီသော ရလဒ် မတွေ့ရှိပါ{zawgyiSuffix}{speedSuffix}");
                }
                _lblDictSummary.ForeColor = allMatches.Count > 0
                    ? Color.FromArgb(20, 85, 185)
                    : Color.FromArgb(165, 65, 20);

                int cardWidth = GetTargetCardWidth(reserveScrollbarForMultipleCards: initialPreparedCards.Length > 2);
                int innerWrap = Math.Max(180, cardWidth - 32);

                if (allMatches.Count == 0 || initialPreparedCards.Length == 0)
                {
                    var emptyCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(215, 130, 30), isSelectableItem: null);
                    emptyCard.SuspendLayout();
                    var lblEmpty = new Label
                    {
                        Text = UiLocalizer.Pick(
                            $"No exact matches found for \"{query}\". Try searching an English word, Myanmar root syllable, or Romanized spelling (e.g., \"mingalaba\").",
                            $"\"{query}\" နှင့် တိုက်ရိုက်ကိုက်ညီသော ဝေါဟာရ မတွေ့ရှိသေးပါ။ အင်္ဂလိပ်စာလုံး၊ မြန်မာစာလုံးအစိတ်အပိုင်း သို့မဟုတ် Romanized အသံထွက် (ဥပမာ \"mingalaba\") ဖြင့် ပြန်လည်ရှာဖွေကြည့်ပါ။"),
                        Font = _fontSmall,
                        ForeColor = Color.FromArgb(70, 78, 92),
                        AutoSize = true,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill
                    };
                    emptyCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    emptyCard.Controls.Add(lblEmpty, 0, 0);
                    emptyCard.ResumeLayout(false);
                    _cardsPanel.Controls.Add(emptyCard);
                    return;
                }

                bool hasMore = allMatches.Count > initialPreparedCards.Length;
                var controlsBatch = new Control[initialPreparedCards.Length + (hasMore ? 1 : 0)];
                for (int i = 0; i < initialPreparedCards.Length; i++)
                {
                    controlsBatch[i] = BuildPreparedDictionaryCard(initialPreparedCards[i], cardWidth, innerWrap);
                }

                if (hasMore)
                {
                    controlsBatch[initialPreparedCards.Length] = CreateShowMoreDictionaryButton(
                        allMatches,
                        nextStartIndex: initialPreparedCards.Length,
                        cardWidth: cardWidth,
                        renderVersion: renderVersion);
                }

                _cardsPanel.Controls.AddRange(controlsBatch);
            }
            finally
            {
                EndCardsBatchUpdate();
            }
        }

        private Button CreateShowMoreDictionaryButton(
            List<DictionaryEntry> allMatches,
            int nextStartIndex,
            int cardWidth,
            int renderVersion)
        {
            int remaining = Math.Max(0, allMatches.Count - nextStartIndex);
            var moreBtn = CreatePillButton(
                UiLocalizer.Pick(
                    $"▾ Show {remaining} More Result(s)",
                    $"▾ နောက်ထပ် ရလဒ် ({remaining}) ခု ဆက်လက်ပြသရန်"),
                Color.FromArgb(235, 243, 255),
                Color.FromArgb(20, 74, 162),
                Color.FromArgb(190, 212, 246),
                _fontSmallBold);
            moreBtn.Width = cardWidth;
            moreBtn.Margin = new Padding(0, 2, 0, 8);
            moreBtn.Click += (s, e) => AppendMoreDictionaryCards(moreBtn, allMatches, nextStartIndex, renderVersion);
            return moreBtn;
        }

        private void AppendMoreDictionaryCards(
            Button clickedMoreBtn,
            List<DictionaryEntry> allMatches,
            int startIndex,
            int renderVersion)
        {
            if (IsDisposed || startIndex >= allMatches.Count)
                return;

            clickedMoreBtn.Enabled = false;
            clickedMoreBtn.Text = UiLocalizer.Pick("⏳ Loading more results...", "⏳ နောက်ထပ် ရလဒ်များ ဖွင့်လှစ်နေသည်...");
            bool isEn = UiLocalizer.IsEnglish;

            if (IsHandleCreated)
            {
                _ = Task.Run(() =>
                {
                    if (renderVersion != Volatile.Read(ref _dictAsyncSearchVersion))
                        return;

                    var nextSlice = PrepareCardsOffThread(allMatches, startIndex, DictionaryAppendBatchSize, isEn);
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (IsDisposed ||
                                    _activeTab != TaskPaneWorkspaceTab.Dictionary ||
                                    renderVersion != Volatile.Read(ref _dictAsyncSearchVersion))
                                {
                                    return;
                                }
                                CommitAppendedDictionarySlice(clickedMoreBtn, allMatches, startIndex, nextSlice, renderVersion);
                            }));
                        }
                    }
                    catch { }
                });
                return;
            }

            var syncSlice = PrepareCardsOffThread(allMatches, startIndex, DictionaryAppendBatchSize, isEn);
            CommitAppendedDictionarySlice(clickedMoreBtn, allMatches, startIndex, syncSlice, renderVersion);
        }

        private void CommitAppendedDictionarySlice(
            Button clickedMoreBtn,
            List<DictionaryEntry> allMatches,
            int startIndex,
            PreparedDictionaryCard[] nextSlice,
            int renderVersion)
        {
            BeginCardsBatchUpdate();
            try
            {
                if (!clickedMoreBtn.IsDisposed)
                {
                    _cardsPanel.Controls.Remove(clickedMoreBtn);
                    clickedMoreBtn.Dispose();
                }

                if (nextSlice.Length == 0)
                    return;

                int cardWidth = GetTargetCardWidth(reserveScrollbarForMultipleCards: true);
                int innerWrap = Math.Max(180, cardWidth - 32);
                int nextRenderedTotal = startIndex + nextSlice.Length;
                bool hasMore = allMatches.Count > nextRenderedTotal;

                var batch = new Control[nextSlice.Length + (hasMore ? 1 : 0)];
                for (int i = 0; i < nextSlice.Length; i++)
                {
                    batch[i] = BuildPreparedDictionaryCard(nextSlice[i], cardWidth, innerWrap);
                }

                if (hasMore)
                {
                    batch[nextSlice.Length] = CreateShowMoreDictionaryButton(allMatches, nextRenderedTotal, cardWidth, renderVersion);
                }

                _cardsPanel.Controls.AddRange(batch);
            }
            finally
            {
                EndCardsBatchUpdate();
            }
        }

        private Control BuildPreparedDictionaryCard(PreparedDictionaryCard prepared, int cardWidth, int innerWrap)
        {
            var entry = prepared.Entry;
            bool isEncyc = entry.IsEncyclopediaEntry;
            bool isProverb = entry.IsProverbOrSayingEntry;
            bool isBilingual = entry.IsBilingualLexiconEntry;
            bool isRefPhrase = entry.IsReferenceEntry;
            bool isPropn = entry.IsProperNoun;
            bool isDomain = entry.IsSpecializedDomain;

            Color accent = isBilingual
                ? Color.FromArgb(20, 88, 168)
                : (isEncyc
                    ? Color.FromArgb(16, 108, 128)
                    : (isProverb
                        ? Color.FromArgb(182, 102, 12)
                        : (isDomain
                            ? Color.FromArgb(88, 52, 168)
                            : (isPropn
                                ? Color.FromArgb(14, 118, 110)
                                : (entry.IsOfficialThatpon ? Color.FromArgb(24, 132, 68) : Color.FromArgb(28, 96, 188))))));

            var card = CreateStyledCardContainer(cardWidth, accent, isSelectableItem: null);
            card.SuspendLayout();
            int dRow = 0;

            string sourceBadge = isBilingual
                ? UiLocalizer.Pick("Bilingual EN ⇄ MM ✓", "အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန် ✓")
                : (isEncyc
                    ? (string.IsNullOrEmpty(entry.SpecializedDomain)
                        ? UiLocalizer.Pick("Encyclopedia ✓", "စွယ်စုံဗဟုသုတကျမ်း ✓")
                        : UiLocalizer.Pick($"Encyclopedia ({entry.SpecializedDomain}) ✓", $"စွယ်စုံဗဟုသုတ ({entry.SpecializedDomain}) ✓"))
                    : (isProverb
                        ? UiLocalizer.Pick("Official MLC Reference ✓", "မြန်မာစာအဖွဲ့ စံကိုးကား ✓")
                        : (isDomain
                            ? UiLocalizer.Pick($"Domain Term ({entry.SpecializedDomain}) ✓", $"ပညာရပ်ဝေါဟာရ ({entry.SpecializedDomain}) ✓")
                            : (isPropn
                                ? UiLocalizer.Pick("Geography / Official Name ✓", "ပထဝီဝင် / ရုံးသုံးအမည်မှန် ✓")
                                : (entry.IsOfficialThatpon
                                    ? UiLocalizer.Pick("Official Thatpon ✓", "သတ်ပုံကျမ်းဝင် ✓")
                                    : (string.Equals(entry.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase)
                                        ? UiLocalizer.Pick("Custom Word ★", "မှတ်သားဝေါဟာရ ★")
                                        : (string.Equals(entry.PosTag, "LOAN", StringComparison.OrdinalIgnoreCase)
                                            ? UiLocalizer.Pick("Loanword", "မွေးစားဝေါဟာရ")
                                            : UiLocalizer.Pick("Standard Lexicon", "ခေတ်သုံး စံဝေါဟာရ"))))))));

            var lblWord = new Label
            {
                Text = $"{entry.Word}   [{entry.LocalizedPosDisplayLabel}]",
                Font = _fontCardWord,
                ForeColor = isBilingual
                    ? Color.FromArgb(14, 56, 128)
                    : (isEncyc
                        ? Color.FromArgb(10, 78, 96)
                        : (isProverb
                            ? Color.FromArgb(128, 68, 8)
                            : (isDomain
                                ? Color.FromArgb(62, 32, 128)
                                : (isPropn ? Color.FromArgb(12, 88, 82) : Color.FromArgb(18, 48, 106))))),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 2),
                Padding = new Padding(2, 2, 2, 2)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblWord, 0, dRow++);

            string metaText;
            if (isBilingual)
            {
                string enPos = entry.EntityCategory;
                metaText = !string.IsNullOrWhiteSpace(enPos)
                    ? $"{sourceBadge}   •   POS: {enPos}"
                    : sourceBadge;
            }
            else if (isRefPhrase)
            {
                metaText = sourceBadge;
            }
            else
            {
                metaText = UiLocalizer.Pick(
                    $"{sourceBadge}   •   Syllables: {prepared.SyllableBreakdown}",
                    $"{sourceBadge}   •   ဝဏ္ဏခွဲ: {prepared.SyllableBreakdown}");
            }

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

            // Compact Dual Pronunciation Line + Offline English TTS Audio Button ([▶ EN])
            string speakEn = prepared.SpeakableEnglish;
            if (prepared.PronInfo.HasValue || !string.IsNullOrEmpty(speakEn))
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
                    Margin = new Padding(0, 0, 0, 5)
                };
                pronRow.SuspendLayout();
                pronRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                pronRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                pronRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                string pronDisplay = prepared.PronInfo.HasValue
                    ? prepared.PronCompactDualLine
                    : UiLocalizer.Pick($"English Audio: {speakEn}", $"အင်္ဂလိပ် အသံထွက်: {speakEn}");
                var lblPron = new Label
                {
                    Text = pronDisplay,
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(38, 56, 88),
                    AutoSize = true,
                    MaximumSize = new Size(Math.Max(120, innerWrap - (!string.IsNullOrEmpty(speakEn) ? 80 : 16)), 0),
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 2, 4, 2)
                };
                if (!string.IsNullOrEmpty(prepared.PronIpaTooltip))
                {
                    _toolTip.SetToolTip(lblPron, prepared.PronIpaTooltip);
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
                    btnSpeakEn.Padding = new Padding(6, 2, 6, 2);
                    _toolTip.SetToolTip(
                        btnSpeakEn,
                        UiLocalizer.Pick(
                            $"Speak English pronunciation offline: \"{capturedSpeakEn}\"",
                            $"\"{capturedSpeakEn}\" ၏ အင်္ဂလိပ်အသံထွက်ကို အင်တာနက်မလိုဘဲ နားထောင်ရန်"));
                    btnSpeakEn.Click += (s, e) => OfflineSpeechService.SpeakEnglishAsync(capturedSpeakEn);
                    pronRow.Controls.Add(btnSpeakEn, 1, 0);
                }

                pronRow.ResumeLayout(false);
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(pronRow, 0, dRow++);
            }

            if (!isBilingual && !string.IsNullOrWhiteSpace(entry.EnglishName))
            {
                var lblEnGloss = new Label
                {
                    Text = $"EN: {entry.EnglishName}",
                    Font = _fontSmallBold,
                    ForeColor = Color.FromArgb(16, 68, 148),
                    BackColor = Color.FromArgb(235, 245, 255),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 5),
                    Padding = new Padding(7, 4, 7, 4)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblEnGloss, 0, dRow++);
            }

            string cueLabelPrefix = isBilingual
                ? UiLocalizer.Pick("Myanmar Meaning(s)", "မြန်မာ အနက်အဓိပ္ပာယ်")
                : (isEncyc
                    ? UiLocalizer.Pick("Encyclopedia Summary", "စွယ်စုံဗဟုသုတ အနှစ်ချုပ်")
                    : (isProverb
                        ? UiLocalizer.Pick("Meaning", "ဆိုလိုရင်း အဓိပ္ပာယ်")
                        : (isDomain
                            ? UiLocalizer.Pick("Domain Definition", "ပညာရပ် အနက် / အသုံး")
                            : (isPropn
                                ? UiLocalizer.Pick("Geography / Info", "ပထဝီဝင် / အချက်အလက်")
                                : UiLocalizer.Pick("Definition / Usage", "မြန်မာအဘိဓာန် အနက် / အသုံး")))));

            var formattedSenses = prepared.FormattedSenses;
            string cueText;
            if (formattedSenses.Count > 1)
            {
                cueText = $"{cueLabelPrefix}:\n" + string.Join("\n", formattedSenses);
            }
            else if (formattedSenses.Count == 1)
            {
                cueText = $"{cueLabelPrefix}: {formattedSenses[0]}";
            }
            else
            {
                cueText = UiLocalizer.Pick(
                    "Usage: Standard Myanmar Language Commission orthography",
                    "အနက် / အသုံး: မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း စံစာလုံးပေါင်း");
            }

            var lblCue = new Label
            {
                Text = cueText,
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

            if (!string.IsNullOrEmpty(prepared.FullUsageDetails))
            {
                string fullMetaText = prepared.FullUsageDetails;
                string collapsedMetaText = prepared.CollapsedUsageDetails;
                bool isCollapsibleMeta = prepared.IsCollapsibleUsage;
                int metaLineCount = prepared.UsageDetailLineCount;

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
                        Text = UiLocalizer.Pick(
                            $"▾ Show Full Metadata ({metaLineCount} items)",
                            $"▾ အချက်အလက် အပြည့်အစုံကြည့်ရန် ({metaLineCount} ချက်)"),
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
                        BeginCardsBatchUpdate();
                        try
                        {
                            lblAbidanMeta.Text = metaExpanded ? fullMetaText : collapsedMetaText;
                            lblToggleMeta.Text = metaExpanded
                                ? UiLocalizer.Pick("▴ Collapse Details", "▴ အကျဉ်းချုပ် ပြန်ပိတ်ရန်")
                                : UiLocalizer.Pick(
                                    $"▾ Show Full Metadata ({metaLineCount} items)",
                                    $"▾ အချက်အလက် အပြည့်အစုံကြည့်ရန် ({metaLineCount} ချက်)");
                        }
                        finally
                        {
                            EndCardsBatchUpdate();
                        }
                    };
                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(lblToggleMeta, 0, dRow++);
                }
            }

            if (prepared.Synonyms.Count > 0)
            {
                var lblSynHeader = new Label
                {
                    Text = isBilingual
                        ? UiLocalizer.Pick("Official Myanmar Headwords (click to open) —", "သတ်ပုံကျမ်းဝင် မြန်မာဝေါဟာရများ (နှိပ်၍ ကြည့်ရန်) —")
                        : UiLocalizer.Pick("Synonyms (click to look up) —", "ကြောင်းတူသံကွဲ (နှိပ်လျှင် ဆက်လက်ရှာဖွေမည်) —"),
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
                synFlow.SuspendLayout();
                var synBtns = new Control[prepared.Synonyms.Count];
                for (int i = 0; i < prepared.Synonyms.Count; i++)
                {
                    string targetSyn = prepared.Synonyms[i];
                    var synPill = CreatePillButton(
                        $"⇄ {targetSyn}",
                        Color.FromArgb(234, 249, 244),
                        Color.FromArgb(14, 96, 76),
                        Color.FromArgb(178, 224, 208),
                        _fontSmall);
                    synPill.Dock = DockStyle.None;
                    _toolTip.SetToolTip(
                        synPill,
                        UiLocalizer.Pick(
                            $"Click to look up \"{targetSyn}\"",
                            $"\"{targetSyn}\" ၏ အနက်နှင့် အသုံးကို ဆက်လက်ရှာဖွေရန် နှိပ်ပါ"));
                    synPill.Click += (s, e) => OpenDictionaryWithQuery(targetSyn);
                    synBtns[i] = synPill;
                }
                synFlow.Controls.AddRange(synBtns);
                synFlow.ResumeLayout(false);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(synFlow, 0, dRow++);
            }

            if (prepared.Antonyms.Count > 0)
            {
                var lblAntHeader = new Label
                {
                    Text = UiLocalizer.Pick("Antonyms (click to look up) —", "ဆန့်ကျင်ဘက် ဝေါဟာရ (နှိပ်လျှင် ဆက်လက်ရှာဖွေမည်) —"),
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
                antFlow.SuspendLayout();
                var antBtns = new Control[prepared.Antonyms.Count];
                for (int i = 0; i < prepared.Antonyms.Count; i++)
                {
                    string targetAnt = prepared.Antonyms[i];
                    var antPill = CreatePillButton(
                        $"≠ {targetAnt}",
                        Color.FromArgb(255, 241, 243),
                        Color.FromArgb(158, 36, 56),
                        Color.FromArgb(242, 196, 204),
                        _fontSmall);
                    antPill.Dock = DockStyle.None;
                    _toolTip.SetToolTip(
                        antPill,
                        UiLocalizer.Pick(
                            $"Click to look up \"{targetAnt}\"",
                            $"\"{targetAnt}\" ၏ အနက်နှင့် အသုံးကို ဆက်လက်ရှာဖွေရန် နှိပ်ပါ"));
                    antPill.Click += (s, e) => OpenDictionaryWithQuery(targetAnt);
                    antBtns[i] = antPill;
                }
                antFlow.Controls.AddRange(antBtns);
                antFlow.ResumeLayout(false);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(antFlow, 0, dRow++);
            }

            if (prepared.Collocations.Count > 0)
            {
                var lblCollocHeader = new Label
                {
                    Text = UiLocalizer.Pick("Common Collocations —", "တွဲဖက်သုံး ဝေါဟာရ —"),
                    Font = _fontSmallBold,
                    ForeColor = Color.FromArgb(72, 44, 148),
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(2, 0, 2, 2)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblCollocHeader, 0, dRow++);

                var collocFlow = new FlowLayoutPanel
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
                collocFlow.SuspendLayout();
                var collocBtns = new Control[prepared.Collocations.Count];
                for (int i = 0; i < prepared.Collocations.Count; i++)
                {
                    string targetColloc = prepared.Collocations[i];
                    var collocPill = CreatePillButton(
                        $"▸ {targetColloc}",
                        Color.FromArgb(244, 242, 255),
                        Color.FromArgb(66, 38, 138),
                        Color.FromArgb(208, 198, 244),
                        _fontSmall);
                    collocPill.Dock = DockStyle.None;
                    _toolTip.SetToolTip(
                        collocPill,
                        UiLocalizer.Pick(
                            $"Common real-world collocation — click to look up \"{targetColloc}\"",
                            $"အသုံးများသော တွဲဖက်ဝေါဟာရ — \"{targetColloc}\" ကို နှိပ်၍ ရှာဖွေပါ"));
                    collocPill.Click += (s, e) => OpenDictionaryWithQuery(targetColloc);
                    collocBtns[i] = collocPill;
                }
                collocFlow.Controls.AddRange(collocBtns);
                collocFlow.ResumeLayout(false);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(collocFlow, 0, dRow++);
            }

            if (prepared.RelatedTerms.Count > 0)
            {
                var lblRelHeader = new Label
                {
                    Text = UiLocalizer.Pick("Related Terms & Topics —", "ဆက်စပ်ဖတ်ရှုဖွယ် ဗဟုသုတ / ဝေါဟာရ —"),
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
                relFlow.SuspendLayout();
                var relBtns = new Control[prepared.RelatedTerms.Count];
                for (int i = 0; i < prepared.RelatedTerms.Count; i++)
                {
                    string targetRel = prepared.RelatedTerms[i];
                    var relPill = CreatePillButton(
                        $"→ {targetRel}",
                        Color.FromArgb(236, 247, 252),
                        Color.FromArgb(16, 86, 112),
                        Color.FromArgb(182, 218, 234),
                        _fontSmall);
                    relPill.Dock = DockStyle.None;
                    _toolTip.SetToolTip(
                        relPill,
                        UiLocalizer.Pick(
                            $"Click to read about \"{targetRel}\"",
                            $"\"{targetRel}\" အကြောင်း ဆက်လက်ဖတ်ရှုရန် နှိပ်ပါ"));
                    relPill.Click += (s, e) => OpenDictionaryWithQuery(targetRel);
                    relBtns[i] = relPill;
                }
                relFlow.Controls.AddRange(relBtns);
                relFlow.ResumeLayout(false);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(relFlow, 0, dRow++);
            }

            if (!string.IsNullOrWhiteSpace(prepared.HomophoneSummary))
            {
                var lblHomo = new Label
                {
                    Text = UiLocalizer.Pick(
                        $"Homophones / Similar Spelling: {prepared.HomophoneSummary}",
                        $"အသံတူ/ဆင်တူ သတိပြုရန်: {prepared.HomophoneSummary}"),
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

            var dictActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 2, 0, 0),
                Padding = new Padding(0)
            };
            dictActions.SuspendLayout();

            string wordToUse = entry.Word;
            bool initialSaved = prepared.IsBookmarked;

            var btnSave = CreatePillButton(
                initialSaved
                    ? UiLocalizer.Pick("★ Saved", "★ မှတ်ပြီး")
                    : UiLocalizer.Pick("☆ Save", "☆ မှတ်မည်"),
                initialSaved ? Color.FromArgb(255, 246, 218) : Color.FromArgb(245, 247, 250),
                initialSaved ? Color.FromArgb(152, 86, 6) : Color.FromArgb(52, 62, 78),
                initialSaved ? Color.FromArgb(238, 204, 128) : Color.FromArgb(215, 222, 232),
                initialSaved ? _fontSmallBold : _fontSmall);
            btnSave.Dock = DockStyle.None;
            _toolTip.SetToolTip(
                btnSave,
                UiLocalizer.Pick(
                    "Bookmark this word (shared automatically across Word & Excel)",
                    "ဤဝေါဟာရကို မှတ်သားထားရန် (Word နှင့် Excel အတူတကွ ချိတ်ဆက်ထားသည်)"));
            btnSave.Click += (s, e) =>
            {
                bool nowSaved = DictionaryBookmarkStore.Shared.ToggleBookmark(wordToUse);
                _dictLookupCache.Clear();
                btnSave.Text = nowSaved
                    ? UiLocalizer.Pick("★ Saved", "★ မှတ်ပြီး")
                    : UiLocalizer.Pick("☆ Save", "☆ မှတ်မည်");
                btnSave.BackColor = nowSaved ? Color.FromArgb(255, 246, 218) : Color.FromArgb(245, 247, 250);
                btnSave.ForeColor = nowSaved ? Color.FromArgb(152, 86, 6) : Color.FromArgb(52, 62, 78);
                btnSave.FlatAppearance.BorderColor = nowSaved ? Color.FromArgb(238, 204, 128) : Color.FromArgb(215, 222, 232);
                btnSave.Font = nowSaved ? _fontSmallBold : _fontSmall;
                UpdateDictionaryNavButtons();
            };

            var btnCopy = CreatePillButton(
                UiLocalizer.Pick("Copy", "ကူးယူမည်"),
                Color.FromArgb(242, 245, 250), Color.FromArgb(48, 58, 74), Color.FromArgb(215, 222, 232), _fontSmall);
            btnCopy.Dock = DockStyle.None;
            btnCopy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(wordToUse);
                    btnCopy.Text = UiLocalizer.Pick("✓ Copied", "✓ ကူးယူပြီး");
                    btnCopy.ForeColor = Color.FromArgb(18, 120, 52);
                }
                catch { }
            };

            var btnInsert = CreatePillButton(
                UiLocalizer.Pick("Insert at Cursor ➔", "စာတမ်းသို့ ထည့်မည် ➔"),
                Color.FromArgb(235, 244, 255),
                Color.FromArgb(20, 68, 148),
                Color.FromArgb(190, 212, 246),
                _fontSmallBold);
            btnInsert.Dock = DockStyle.None;
            btnInsert.Click += (s, e) => RequestInsertWordAtCursor?.Invoke(wordToUse);

            dictActions.Controls.AddRange(new Control[] { btnSave, btnCopy, btnInsert });
            dictActions.ResumeLayout(false);

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(dictActions, 0, dRow++);
            card.ResumeLayout(false);

            return card;
        }
    }
}
