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

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    public sealed partial class ExcelProofingTaskPaneControl
    {
        // ================= EXCEL DICTIONARY & PROVERBS EXPLORER (ZERO-UI-FREEZE PIPELINE) =================

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
                maxSynonymsPerCard: 6,
                maxAntonymsPerCard: 4,
                maxCollocationsPerCard: 5,
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

                int cardWidth = GetTargetCardWidth();
                int innerWrap = Math.Max(180, cardWidth - 32);
                var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188));
                card.SuspendLayout();
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(new Label
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
                    Tag = WrapTag
                }, 0, 0);
                card.ResumeLayout(false);
                _cardsPanel.Controls.Add(card);
            }
            finally
            {
                EndCardsBatchUpdate();
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

                    int cardWidth = GetTargetCardWidth();
                    int innerWrap = Math.Max(180, cardWidth - 32);

                    _lblDictSummary.Text = UiLocalizer.Pick(
                        "★ Saved Dictionary Bookmarks (0) — synced across Word & Excel",
                        "★ မှတ်သားထားသော အဘိဓာန် ဝေါဟာရများ (0) — Word နှင့် Excel ချိတ်ဆက်ထားသည်");

                    var empty = CreateStyledCardContainer(cardWidth, Color.FromArgb(196, 118, 12));
                    empty.SuspendLayout();
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
                    empty.ResumeLayout(false);
                    _cardsPanel.Controls.Add(empty);
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

        private void LoadFeaturedReferenceExcelView(bool isProverbs, string titleEn, string titleMm, string? fallbackQuery)
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

                _ = Task.Run(() =>
                {
                    if (ver != Volatile.Read(ref _dictAsyncSearchVersion))
                        return;

                    var sw = Stopwatch.StartNew();
                    var asyncFeatured = provider?.Invoke() ?? new List<DictionaryEntry>();
                    var prepared = PrepareCardsOffThread(asyncFeatured, 0, InitialDictionaryRenderLimit, isEn);
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
                                if (asyncFeatured.Count > 0)
                                {
                                    RenderPreparedDictionaryResults(
                                        UiLocalizer.Pick(titleEn, titleMm),
                                        asyncFeatured,
                                        prepared,
                                        ms,
                                        convertedFromZawgyi: false,
                                        effectiveQuery: string.Empty,
                                        isCustomHeaderTitle: true,
                                        renderVersion: ver);
                                }
                                else if (!string.IsNullOrWhiteSpace(fallbackQuery))
                                {
                                    SetDictionarySearchTextSilently(fallbackQuery);
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
            var featured = provider?.Invoke() ?? new List<DictionaryEntry>();
            var syncPrepared = PrepareCardsOffThread(featured, 0, InitialDictionaryRenderLimit, isEn);
            syncSw.Stop();
            if (featured.Count > 0)
            {
                RenderPreparedDictionaryResults(
                    UiLocalizer.Pick(titleEn, titleMm),
                    featured,
                    syncPrepared,
                    syncSw.Elapsed.TotalMilliseconds,
                    convertedFromZawgyi: false,
                    effectiveQuery: string.Empty,
                    isCustomHeaderTitle: true,
                    renderVersion: ver);
                return;
            }

            if (!string.IsNullOrWhiteSpace(fallbackQuery))
            {
                SetDictionarySearchTextSilently(fallbackQuery);
                ExecuteDictionarySearchNow();
            }
        }

        private void SetDictionaryCategory(DictionaryCategoryFilter filter)
        {
            _showingSavedBookmarksView = false;
            _dictCategoryFilter = filter;
            UpdateDictionaryCategoryStyles();
            UpdateDictionaryNavButtons();

            if (filter == DictionaryCategoryFilter.EncyclopediaKnowledge && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                LoadFeaturedReferenceExcelView(
                    isProverbs: false,
                    "Encyclopedia (Featured)",
                    "စွယ်စုံဗဟုသုတ (လေ့လာဖွယ်)",
                    fallbackQuery: "မြန်မာသဒ္ဒါ");
                return;
            }

            if (filter == DictionaryCategoryFilter.ProverbsAndSayings && string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
            {
                LoadFeaturedReferenceExcelView(
                    isProverbs: true,
                    "Myanmar Proverbs & Sayings (Featured)",
                    "မြန်မာစကားပုံနှင့် ဆိုရိုးစကားများ (လေ့လာဖွယ်)",
                    fallbackQuery: "စကားပုံ");
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
            _searchDebounceTimer.Stop();
            if (_activeTab != TaskPaneWorkspaceTab.Dictionary)
                return;

            int ver = Interlocked.Increment(ref _dictAsyncSearchVersion);
            _showingSavedBookmarksView = false;
            string query = _txtDictionarySearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                UpdateDictionaryNavButtons();
                RenderDictionaryDiscoveryCards(ver);
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

                _ = Task.Run(() =>
                {
                    if (ver != Volatile.Read(ref _dictAsyncSearchVersion))
                        return;

                    string effectiveQuery = MyanmarTrie.NormalizeSearchQuery(query, out bool convertedFromZawgyi);
                    var (asyncFiltered, asyncMs) = ExecuteExcelDictionaryQueryCore(query, effectiveQuery, categorySnapshot);

                    if (ver != Volatile.Read(ref _dictAsyncSearchVersion))
                        return;

                    var preparedInitial = PrepareCardsOffThread(asyncFiltered, 0, InitialDictionaryRenderLimit, isEn);
                    long currentMutVer = RequestGetEngineMutationVersion?.Invoke() ?? mutVer;
                    _dictLookupCache.Store(
                        currentMutVer,
                        isEn,
                        (int)categorySnapshot,
                        query,
                        new CachedDictionaryQueryView(query, effectiveQuery, convertedFromZawgyi, asyncMs, asyncFiltered, preparedInitial));

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
            var (syncFiltered, syncElapsedMs) = ExecuteExcelDictionaryQueryCore(query, syncEffective, categorySnapshot);
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

        private void RenderDictionaryDiscoveryCards(int renderVersion)
        {
            TriggerBackgroundDictionaryPrewarmIfNeeded();

            BeginCardsBatchUpdate();
            try
            {
                DisposeAndClearCardsPanel();

                int cardWidth = GetTargetCardWidth();
                int innerWrap = Math.Max(180, cardWidth - 32);

                _lblDictSummary.Text = UiLocalizer.Pick(
                    "Bilingual EN ⇄ MM (57,000+), Official Thatpon (95,000+), Encyclopedia (25,000+) & Romanized Search",
                    "အင်္ဂလိပ် ⇄ မြန်မာ (၅၇,၀၀၀+)၊ သတ်ပုံကျမ်း (၉၅,၀၀၀+)၊ စွယ်စုံကျမ်း (၂၅,၀၀၀+) နှင့် အသံထွက်ရှာဖွေမှု");

                var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188));
                card.SuspendLayout();
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
                sampleFlow.SuspendLayout();

                string[] samples = { "ဘဏ္ဍာရေး", "abandon", "mingalaba", "negotiate", "စာရင်းအင်း", "မြန်မာသဒ္ဒါ", "ကျောက်ဆည်" };
                var sampleChips = new Control[samples.Length];
                for (int i = 0; i < samples.Length; i++)
                {
                    string captured = samples[i];
                    var chip = CreatePillButton(
                        $"▸ {captured}",
                        Color.FromArgb(238, 244, 255),
                        Color.FromArgb(22, 74, 158),
                        Color.FromArgb(196, 214, 245),
                        _fontBrandStats);
                    chip.Click += (sender, e) => OpenDictionaryWithQuery(captured);
                    sampleChips[i] = chip;
                }
                sampleFlow.Controls.AddRange(sampleChips);
                sampleFlow.ResumeLayout(false);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(sampleFlow, 0, r++);
                card.ResumeLayout(false);
                _cardsPanel.Controls.Add(card);
            }
            finally
            {
                EndCardsBatchUpdate();
            }

            if (IsDictionaryEngineStillWarmingUp() || RequestGetFeaturedKnowledge == null)
                return;

            bool isEn = UiLocalizer.IsEnglish;
            if (IsHandleCreated && !IsDisposed)
            {
                _ = Task.Run(() =>
                {
                    if (renderVersion != Volatile.Read(ref _dictAsyncSearchVersion))
                        return;

                    var asyncFeatured = RequestGetFeaturedKnowledge?.Invoke() ?? new List<DictionaryEntry>();
                    var preparedFeatured = PrepareCardsOffThread(asyncFeatured, 0, 4, isEn);
                    if (preparedFeatured.Length == 0)
                        return;

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
                                    renderVersion != Volatile.Read(ref _dictAsyncSearchVersion))
                                {
                                    return;
                                }

                                BeginCardsBatchUpdate();
                                try
                                {
                                    int cw = GetTargetCardWidth(reserveScrollbarForMultipleCards: true);
                                    int iw = Math.Max(180, cw - 32);
                                    var extraCards = new Control[preparedFeatured.Length];
                                    for (int i = 0; i < preparedFeatured.Length; i++)
                                    {
                                        extraCards[i] = BuildPreparedDictionaryEntryCard(preparedFeatured[i], cw, iw);
                                    }
                                    _cardsPanel.Controls.AddRange(extraCards);
                                }
                                finally
                                {
                                    EndCardsBatchUpdate();
                                }
                            }));
                        }
                    }
                    catch { }
                });
                return;
            }

            var syncFeatured = RequestGetFeaturedKnowledge.Invoke();
            var syncPreparedFeatured = PrepareCardsOffThread(syncFeatured, 0, 4, isEn);
            if (syncPreparedFeatured.Length > 0)
            {
                BeginCardsBatchUpdate();
                try
                {
                    int cw = GetTargetCardWidth(reserveScrollbarForMultipleCards: true);
                    int iw = Math.Max(180, cw - 32);
                    var extraCards = new Control[syncPreparedFeatured.Length];
                    for (int i = 0; i < syncPreparedFeatured.Length; i++)
                    {
                        extraCards[i] = BuildPreparedDictionaryEntryCard(syncPreparedFeatured[i], cw, iw);
                    }
                    _cardsPanel.Controls.AddRange(extraCards);
                }
                finally
                {
                    EndCardsBatchUpdate();
                }
            }
        }

        private void RenderPreparedDictionaryResults(
            string query,
            List<DictionaryEntry> entries,
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

                int cardWidth = GetTargetCardWidth(reserveScrollbarForMultipleCards: initialPreparedCards.Length > 2);
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

                if (entries.Count == 0 || initialPreparedCards.Length == 0)
                {
                    var empty = CreateStyledCardContainer(cardWidth, Color.FromArgb(180, 120, 24));
                    empty.SuspendLayout();
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
                    empty.ResumeLayout(false);
                    _cardsPanel.Controls.Add(empty);
                    return;
                }

                bool hasMore = entries.Count > initialPreparedCards.Length;
                var controlsBatch = new Control[initialPreparedCards.Length + (hasMore ? 1 : 0)];
                for (int i = 0; i < initialPreparedCards.Length; i++)
                {
                    controlsBatch[i] = BuildPreparedDictionaryEntryCard(initialPreparedCards[i], cardWidth, innerWrap);
                }

                if (hasMore)
                {
                    controlsBatch[initialPreparedCards.Length] = CreateShowMoreExcelDictionaryButton(
                        entries,
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

        private Button CreateShowMoreExcelDictionaryButton(
            List<DictionaryEntry> allEntries,
            int nextStartIndex,
            int cardWidth,
            int renderVersion)
        {
            int remaining = Math.Max(0, allEntries.Count - nextStartIndex);
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
            moreBtn.Click += (s, e) => AppendMoreExcelDictionaryCards(moreBtn, allEntries, nextStartIndex, renderVersion);
            return moreBtn;
        }

        private void AppendMoreExcelDictionaryCards(
            Button clickedMoreBtn,
            List<DictionaryEntry> allEntries,
            int startIndex,
            int renderVersion)
        {
            if (IsDisposed || startIndex >= allEntries.Count)
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

                    var nextSlice = PrepareCardsOffThread(allEntries, startIndex, DictionaryAppendBatchSize, isEn);
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
                                CommitAppendedExcelDictionarySlice(clickedMoreBtn, allEntries, startIndex, nextSlice, renderVersion);
                            }));
                        }
                    }
                    catch { }
                });
                return;
            }

            var syncSlice = PrepareCardsOffThread(allEntries, startIndex, DictionaryAppendBatchSize, isEn);
            CommitAppendedExcelDictionarySlice(clickedMoreBtn, allEntries, startIndex, syncSlice, renderVersion);
        }

        private void CommitAppendedExcelDictionarySlice(
            Button clickedMoreBtn,
            List<DictionaryEntry> allEntries,
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
                bool hasMore = allEntries.Count > nextRenderedTotal;

                var batch = new Control[nextSlice.Length + (hasMore ? 1 : 0)];
                for (int i = 0; i < nextSlice.Length; i++)
                {
                    batch[i] = BuildPreparedDictionaryEntryCard(nextSlice[i], cardWidth, innerWrap);
                }

                if (hasMore)
                {
                    batch[nextSlice.Length] = CreateShowMoreExcelDictionaryButton(allEntries, nextRenderedTotal, cardWidth, renderVersion);
                }

                _cardsPanel.Controls.AddRange(batch);
            }
            finally
            {
                EndCardsBatchUpdate();
            }
        }

        private Control BuildPreparedDictionaryEntryCard(PreparedDictionaryCard prepared, int cardWidth, int innerWrap)
        {
            var entry = prepared.Entry;
            bool isBilingual = entry.IsBilingualLexiconEntry;
            Color stripe = isBilingual
                ? Color.FromArgb(20, 88, 168)
                : (entry.IsEncyclopediaEntry
                    ? Color.FromArgb(124, 58, 202)
                    : (entry.IsProverbOrSayingEntry ? Color.FromArgb(196, 118, 12) : Color.FromArgb(24, 90, 188)));

            var card = CreateStyledCardContainer(cardWidth, stripe);
            card.SuspendLayout();
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
            topRow.SuspendLayout();
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
            topRow.ResumeLayout(false);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(topRow, 0, r++);

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
                    Margin = new Padding(0, 0, 0, 4)
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
                    Font = _fontBrandStats,
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
                    _toolTip.SetToolTip(
                        btnSpeakEn,
                        UiLocalizer.Pick(
                            $"Speak English pronunciation offline: \"{capturedSpeakEn}\"",
                            $"\"{capturedSpeakEn}\" ၏ အင်္ဂလိပ်အသံထွက် နားထောင်ရန် (Offline)"));
                    btnSpeakEn.Click += (s, e) => OfflineSpeechService.SpeakEnglishAsync(capturedSpeakEn);
                    pronRow.Controls.Add(btnSpeakEn, 1, 0);
                }

                pronRow.ResumeLayout(false);
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

            if (prepared.FormattedSenses.Count > 0)
            {
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(new Label
                {
                    Text = string.Join("\n", prepared.FormattedSenses),
                    Font = _fontBody,
                    ForeColor = Color.FromArgb(30, 40, 56),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Margin = new Padding(0, 0, 0, 4)
                }, 0, r++);
            }

            if (!string.IsNullOrEmpty(prepared.CollapsedUsageDetails))
            {
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(new Label
                {
                    Text = prepared.CollapsedUsageDetails,
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(66, 78, 96),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Margin = new Padding(0, 0, 0, 5)
                }, 0, r++);
            }

            if (!string.IsNullOrWhiteSpace(prepared.HomophoneSummary))
            {
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(new Label
                {
                    Text = UiLocalizer.Pick(
                        $"Homophone Comparison — {prepared.HomophoneSummary}",
                        $"သံတူကြောင်းကွဲ နှိုင်းယှဉ်ချက် — {prepared.HomophoneSummary}"),
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

            int relCount = prepared.Synonyms.Count + prepared.Antonyms.Count;
            if (relCount > 0)
            {
                var relFlow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = true,
                    Margin = new Padding(0, 0, 0, 4)
                };
                relFlow.SuspendLayout();
                var relChips = new Control[relCount];
                int idx = 0;
                for (int i = 0; i < prepared.Synonyms.Count; i++)
                {
                    string cap = prepared.Synonyms[i];
                    var chip = CreatePillButton($"⇄ {cap}", Color.FromArgb(236, 246, 240), Color.FromArgb(16, 102, 52), Color.FromArgb(186, 222, 198), _fontBrandStats);
                    chip.Click += (s, e) => OpenDictionaryWithQuery(cap);
                    relChips[idx++] = chip;
                }
                for (int i = 0; i < prepared.Antonyms.Count; i++)
                {
                    string cap = prepared.Antonyms[i];
                    var chip = CreatePillButton($"≠ {cap}", Color.FromArgb(254, 240, 240), Color.FromArgb(168, 32, 32), Color.FromArgb(236, 198, 198), _fontBrandStats);
                    chip.Click += (s, e) => OpenDictionaryWithQuery(cap);
                    relChips[idx++] = chip;
                }
                relFlow.Controls.AddRange(relChips);
                relFlow.ResumeLayout(false);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(relFlow, 0, r++);
            }

            if (prepared.Collocations.Count > 0)
            {
                var collocFlow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = true,
                    Margin = new Padding(0, 0, 0, 4)
                };
                collocFlow.SuspendLayout();
                var collocChips = new Control[prepared.Collocations.Count];
                for (int i = 0; i < prepared.Collocations.Count; i++)
                {
                    string cap = prepared.Collocations[i];
                    var chip = CreatePillButton($"▸ {cap}", Color.FromArgb(244, 242, 255), Color.FromArgb(66, 38, 138), Color.FromArgb(208, 198, 244), _fontBrandStats);
                    _toolTip.SetToolTip(
                        chip,
                        UiLocalizer.Pick(
                            $"Common collocation — click to look up \"{cap}\"",
                            $"အသုံးများသော တွဲဖက်ဝေါဟာရ — \"{cap}\" ကို နှိပ်၍ ရှာဖွေပါ"));
                    chip.Click += (s, e) => OpenDictionaryWithQuery(cap);
                    collocChips[i] = chip;
                }
                collocFlow.Controls.AddRange(collocChips);
                collocFlow.ResumeLayout(false);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(collocFlow, 0, r++);
            }

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true
            };
            btnRow.SuspendLayout();

            string wordToUse = entry.Word;
            bool initialSaved = prepared.IsBookmarked;
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
                _dictLookupCache.Clear();
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

            btnRow.Controls.AddRange(new Control[] { btnSave, btnCopy, btnInsert });
            btnRow.ResumeLayout(false);

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnRow, 0, r++);
            card.ResumeLayout(false);

            return card;
        }
    }
}
