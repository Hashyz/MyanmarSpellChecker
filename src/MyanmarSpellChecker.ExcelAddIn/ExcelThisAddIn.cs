// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Extensibility;
using Microsoft.Win32;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Security;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.ExcelAddIn.Interop;
using MyanmarSpellChecker.ExcelAddIn.UI;
using Excel = Microsoft.Office.Interop.Excel;
using Office = Microsoft.Office.Core;

namespace MyanmarSpellChecker.ExcelAddIn
{
    /// <summary>
    /// Enterprise COM Add-in Entry Point for Microsoft Excel (Desktop Windows, 32-bit &amp; 64-bit AnyCPU).
    /// Shares the exact same AES-256 + HMAC-SHA256 encrypted dictionary (mlc_thatpon.db), style mode,
    /// custom words, and offline RSA-2048 license as MyanmarSpellChecker.WordAddIn while enforcing
    /// strict Formula-Cell Protection, In-Cell Edit-Mode Guards, Read-Only Bulk 2D Scanning, and Multi-Level Undo.
    /// </summary>
    [ComVisible(true)]
    [Guid("9A6B4D53-7E22-4C8A-AD91-8C23F6E05B22")]
    [ProgId("MyanmarSpellChecker.ExcelAddIn")]
    public class ExcelThisAddIn : IDTExtensibility2, Office.IRibbonExtensibility, Office.ICustomTaskPaneConsumer
    {
        public const string DefaultSqlCipherKey = "MM-GOV-THATPON-SQLCIPHER-2026!";

        public const string DefaultVendorPublicKeyXml =
            "<RSAKeyValue><Modulus>w6uP9e7V5r2X1a9K0l8m3N4b5V6c7X8z9A0s1D2f3G4h5J6k7L8p9O0i1U2y3T4r5E6w7Q8a9S0d1F2g3H4j5K6l7Z8x9C0v1B2n3M4q5W6e7R8t9Y0u1I2o3P4a5S6d7F8g9H0j1K2l3Z4x5C6v7B8n9M0q1W2e3R4t5Y6u7I8o9P0a1S2d3F4g5H6j7K8l9Z0x1C2v3B4n5M6q7W8e9R0t1Y2u3I4o5P6a7S8d9F0g1H2j3K4l5Z6x7C8v9B0n1M2q3W4e5R6t7Y8u9I0o1P2a3S4d5F6g7H8j9K0l1Z2x3C4v5B6n7M8q9W0e1R2t3Y4u5I6o7P8a9S0d1F2g3H4j5K6l7Z8==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private static readonly object s_instanceGate = new object();
        private static bool s_primaryInstanceActive;

        private bool _isPrimaryInstance;
        private Excel.Application? _excelApp;
        private Office.IRibbonUI? _ribbonUi;
        private Office.ICTPFactory? _ctpFactory;
        private Control? _uiMarshaller;
        private Timer? _startupTaskPaneTimer;
        private int _startupTaskPaneRetries;

        private MyanmarProofingEngine? _proofingEngine;
        private ExcelWorkbookScannerAndToolkit? _scannerToolkit;
        private ProofingStyleMode _pendingStyleMode = ProofingStyleMode.GeneralStandard;
        private bool _pendingLiveAutoScan = true;
        private bool _pendingVisualUnderlines = true;

        // Per-Window Task Pane map (keyed by Excel.Window.Hwnd) for Multi-Workbook SDI support
        private readonly Dictionary<int, (Office.CustomTaskPane Pane, ExcelProofingTaskPaneControl Control)> _windowTaskPanes =
            new Dictionary<int, (Office.CustomTaskPane Pane, ExcelProofingTaskPaneControl Control)>();

        private string _appDataFolder = string.Empty;
        private string _styleModeConfigPath = string.Empty;
        private string _visualUnderlineConfigPath = string.Empty;
        private string _customDictFilePath = string.Empty;
        private string _licenseFilePath = string.Empty;
        private string _databaseFilePath = string.Empty;
        private string _activeDatabaseKey = string.Empty;
        private DateTime _loadedDbLastWriteUtc = DateTime.MinValue;
        private int _dbHotReloadInProgress;

        static ExcelThisAddIn()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string? addInDir = Path.GetDirectoryName(typeof(ExcelThisAddIn).Assembly.Location);
                    if (string.IsNullOrEmpty(addInDir))
                        return null;

                    var asmName = new System.Reflection.AssemblyName(args.Name);
                    string candidatePath = Path.Combine(addInDir, asmName.Name + ".dll");
                    if (File.Exists(candidatePath))
                    {
                        return System.Reflection.Assembly.LoadFrom(candidatePath);
                    }
                }
                catch
                {
                    // Ignore
                }
                return null;
            };
        }

        public void OnConnection(object application, ext_ConnectMode connectMode, object addInInst, ref Array custom)
        {
            lock (s_instanceGate)
            {
                if (s_primaryInstanceActive)
                {
                    _isPrimaryInstance = false;
                    return;
                }
                s_primaryInstanceActive = true;
                _isPrimaryInstance = true;
            }

            _excelApp = application as Excel.Application;
            if (_excelApp == null)
                return;

            AddInDiagnostics.SafeExecute("OnConnection", () =>
            {
                _uiMarshaller = new Control();
                IntPtr _ = _uiMarshaller.Handle;

                InitializePathsAndStartAsyncEngineLoad();
                HookExcelEvents();
            });
        }

        private void InitializePathsAndStartAsyncEngineLoad()
        {
            string? installDir = Path.GetDirectoryName(typeof(ExcelThisAddIn).Assembly.Location);
            string programDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyanmarSpellChecker");
            string localAppDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyanmarSpellChecker");

            _appDataFolder = Directory.Exists(programDataDir) ? programDataDir : localAppDataDir;
            Directory.CreateDirectory(_appDataFolder);
            Directory.CreateDirectory(localAppDataDir);
            AddInDiagnostics.ConfigureLogDirectory(localAppDataDir);

            _styleModeConfigPath = Path.Combine(localAppDataDir, "style_mode.txt");
            _visualUnderlineConfigPath = Path.Combine(localAppDataDir, "excel_underlines.txt");
            _customDictFilePath = Path.Combine(localAppDataDir, "custom_dictionary.txt");
            string ministryCustomDictPath = Path.Combine(programDataDir, "ministry_custom_dictionary.txt");

            string installLic = !string.IsNullOrEmpty(installDir) ? Path.Combine(installDir, "license.lic") : string.Empty;
            string progDataLic = Path.Combine(programDataDir, "license.lic");
            _licenseFilePath = File.Exists(installLic)
                ? installLic
                : (File.Exists(progDataLic) ? progDataLic : Path.Combine(_appDataFolder, "license.lic"));

            string installDb = !string.IsNullOrEmpty(installDir) ? Path.Combine(installDir, "mlc_thatpon.db") : string.Empty;
            string progDataDb = Path.Combine(programDataDir, "mlc_thatpon.db");
            string localAppDb = Path.Combine(localAppDataDir, "mlc_thatpon.db");

            var candidateDbs = new[] { installDb, progDataDb, localAppDb }
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.Length >= 100_000 ? 1 : 0)
                .ThenByDescending(fi => fi.LastWriteTimeUtc)
                .ThenByDescending(fi => fi.Length)
                .ToList();

            _databaseFilePath = candidateDbs.Count > 0
                ? candidateDbs[0].FullName
                : Path.Combine(_appDataFolder, "mlc_thatpon.db");

            _pendingStyleMode = LoadSavedStyleMode();
            _pendingVisualUnderlines = LoadSavedVisualUnderlineSetting();

            string dbPathSnapshot = _databaseFilePath;
            string licPathSnapshot = _licenseFilePath;
            string userDictSnapshot = _customDictFilePath;
            ProofingStyleMode initialMode = _pendingStyleMode;

            // Decrypt mlc_thatpon.db, build 89,522-headword Trie, and pre-warm search indexes completely off Excel's UI thread!
            Task.Run(() =>
            {
                AddInDiagnostics.SafeExecute("AsyncEngineWarmup", () =>
                {
                    SuppressOfficeMissingBurmeseBannerRegistry();

                    string activeDbKey = AntiTamperGuard.ReconstructProtectedFallbackKey();
                    if (File.Exists(licPathSnapshot))
                    {
                        string licJson = File.ReadAllText(licPathSnapshot);
                        var licResult = LicenseService.ValidateLicenseJson(licJson, DefaultVendorPublicKeyXml);
                        if (licResult.IsValid && !string.IsNullOrWhiteSpace(licResult.SqlCipherDatabaseKey))
                        {
                            activeDbKey = licResult.SqlCipherDatabaseKey!;
                        }
                    }

                    var repo = new EncryptedDictionaryRepository(dbPathSnapshot);
                    repo.EnsureDatabaseExists(activeDbKey);
                    MyanmarTrie trie = repo.LoadIntoTrie(activeDbKey);
                    DateTime loadedStampUtc = File.Exists(dbPathSnapshot)
                        ? File.GetLastWriteTimeUtc(dbPathSnapshot)
                        : DateTime.MinValue;

                    var engine = new MyanmarProofingEngine(trie, initialMode);
                    engine.LoadCustomDictionaryFile(ministryCustomDictPath);
                    engine.LoadCustomDictionaryFile(userDictSnapshot);
                    trie.PrewarmSearchIndexes();

                    Control? marshaller = _uiMarshaller;
                    if (marshaller != null && marshaller.IsHandleCreated && !marshaller.IsDisposed)
                    {
                        marshaller.BeginInvoke(new Action(() =>
                        {
                            _activeDatabaseKey = activeDbKey;
                            _loadedDbLastWriteUtc = loadedStampUtc;
                            AddInDiagnostics.SafeExecute("AttachEngineOnStaThread", () => AttachEngineOnStaThread(engine));
                        }));
                    }
                });
            });
        }

        private void AttachEngineOnStaThread(MyanmarProofingEngine engine)
        {
            if (!_isPrimaryInstance || _excelApp == null)
                return;

            engine.StyleMode = _pendingStyleMode;
            _proofingEngine = engine;

            _scannerToolkit = new ExcelWorkbookScannerAndToolkit(_excelApp, _proofingEngine)
            {
                IsLiveAutoScanEnabled = _pendingLiveAutoScan,
                IsVisualUnderlineEnabled = _pendingVisualUnderlines
            };

            _scannerToolkit.ScanStarted += () =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.SetScanningState(true);
            };
            _scannerToolkit.ScanCompleted += items =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.RenderIssues(items);
            };
            _scannerToolkit.UndoStateChanged += SyncUndoStateAcrossTaskPanes;

            foreach (var kvp in _windowTaskPanes.Values)
            {
                kvp.Control.SetLexiconStats(
                    _proofingEngine.Trie.WordCount,
                    _proofingEngine.Trie.OfficialThatponCount,
                    _proofingEngine.Trie.RuleCount,
                    _proofingEngine.Trie.ReferenceCount,
                    _proofingEngine.Trie.EncyclopediaCount);
                kvp.Control.SetStyleMode(_proofingEngine.StyleMode);
                kvp.Control.RefreshActiveTabAfterEngineReady();
            }

            _ribbonUi?.Invalidate();

            // Perform initial read-only scan if a workbook is already open
            try
            {
                if (_excelApp.ActiveWorkbook != null && ExcelWorkbookScannerAndToolkit.IsExcelReadyForInterop(_excelApp))
                {
                    _scannerToolkit.ScanScope(SpreadsheetScanScope.ActiveSheet);
                }
            }
            catch { }
        }

        private void HookExcelEvents()
        {
            if (_excelApp == null)
                return;

            _excelApp.SheetChange += OnSheetChange;
            _excelApp.SheetActivate += OnSheetActivate;
            _excelApp.WindowActivate += OnWindowActivate;
            _excelApp.WorkbookOpen += OnWorkbookOpen;
            ((Excel.AppEvents_Event)_excelApp).NewWorkbook += OnNewWorkbook;
            _excelApp.WorkbookBeforeSave += OnWorkbookBeforeSave;
            _excelApp.WorkbookBeforePrint += OnWorkbookBeforePrint;
            _excelApp.WorkbookBeforeClose += OnWorkbookBeforeClose;
        }

        private void OnWorkbookBeforeSave(Excel.Workbook wb, bool saveAsUI, ref bool cancel)
        {
            AddInDiagnostics.SafeExecute("OnWorkbookBeforeSave", () =>
            {
                // Strip temporary Red/Blue/Amber proofing underlines before Excel writes the .xlsx file,
                // then restore them 450ms later if the workbook remains open
                _scannerToolkit?.TemporarilyStripUnderlinesForSaveOrPrint(wb);
            });
        }

        private void OnWorkbookBeforePrint(Excel.Workbook wb, ref bool cancel)
        {
            AddInDiagnostics.SafeExecute("OnWorkbookBeforePrint", () =>
            {
                // Strip temporary proofing underlines before printing, then restore 450ms later
                _scannerToolkit?.TemporarilyStripUnderlinesForSaveOrPrint(wb);
            });
        }

        private void OnSheetChange(object sh, Excel.Range target)
        {
            AddInDiagnostics.SafeExecute("OnSheetChange", () =>
            {
                _scannerToolkit?.OnWorkbookSheetChange(sh, target);
            });
        }

        private void OnSheetActivate(object sh)
        {
            AddInDiagnostics.SafeExecute("OnSheetActivate", () =>
            {
                if (_scannerToolkit != null &&
                    _scannerToolkit.IsLiveAutoScanEnabled &&
                    _scannerToolkit.CurrentScope == SpreadsheetScanScope.ActiveSheet)
                {
                    _scannerToolkit.ScanScope(SpreadsheetScanScope.ActiveSheet);
                }
            });
        }

        private void OnWindowActivate(Excel.Workbook wb, Excel.Window wn)
        {
            AddInDiagnostics.SafeExecute("OnWindowActivate", () =>
            {
                PruneClosedWindowTaskPanes();
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                if (ctrl != null && _scannerToolkit != null)
                {
                    ctrl.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                    ctrl.SetUndoState(_scannerToolkit.UndoManager.UndoCount, _scannerToolkit.UndoManager.LatestDescription);
                }
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
            });
        }

        private void OnWorkbookOpen(Excel.Workbook wb)
        {
            AddInDiagnostics.SafeExecute("OnWorkbookOpen", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                _scannerToolkit?.ScanScope(SpreadsheetScanScope.ActiveSheet);
            });
        }

        private void OnNewWorkbook(Excel.Workbook wb)
        {
            AddInDiagnostics.SafeExecute("OnNewWorkbook", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            });
        }

        private void OnWorkbookBeforeClose(Excel.Workbook wb, ref bool cancel)
        {
            AddInDiagnostics.SafeExecute("OnWorkbookBeforeClose", () =>
            {
                string? wbName = null;
                try { wbName = wb?.Name; } catch { }
                _scannerToolkit?.PurgeClosedWorkbook(wbName);
                PruneClosedWindowTaskPanes();
            });
        }

        private void SyncUndoStateAcrossTaskPanes()
        {
            int count = _scannerToolkit?.UndoManager.UndoCount ?? 0;
            string desc = _scannerToolkit?.UndoManager.LatestDescription ?? string.Empty;
            foreach (var kv in _windowTaskPanes.Values)
            {
                kv.Control.SetUndoState(count, desc);
            }
            _ribbonUi?.InvalidateControl("BtnUndoLastFix");
        }

        private void PruneClosedWindowTaskPanes()
        {
            if (_excelApp == null || _windowTaskPanes.Count == 0)
                return;

            try
            {
                var liveHwnds = new HashSet<int>();
                foreach (Excel.Window win in _excelApp.Windows)
                {
                    try
                    {
                        if (win.Hwnd != 0)
                            liveHwnds.Add(win.Hwnd);
                    }
                    catch { }
                }

                var stale = _windowTaskPanes.Keys.Where(h => !liveHwnds.Contains(h)).ToList();
                foreach (int h in stale)
                    _windowTaskPanes.Remove(h);
            }
            catch { }
        }

        public void CTPFactoryAvailable(Office.ICTPFactory ctpFactoryInst)
        {
            if (!_isPrimaryInstance)
                return;

            AddInDiagnostics.SafeExecute("CTPFactoryAvailable", () =>
            {
                _ctpFactory = ctpFactoryInst;
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            });
        }

        private ExcelProofingTaskPaneControl? GetOrCreateActiveWindowTaskPaneControl(bool autoShow)
        {
            if (!_isPrimaryInstance || _ctpFactory == null || _excelApp == null)
                return null;

            try
            {
                Excel.Window? activeWin = null;
                try { activeWin = _excelApp.ActiveWindow; } catch { }
                int hwnd = activeWin?.Hwnd ?? 0;

                if (activeWin == null || hwnd == 0)
                {
                    if (autoShow)
                        ScheduleDeferredTaskPaneCreation();
                    return null;
                }

                if (_windowTaskPanes.TryGetValue(hwnd, out var existing))
                {
                    if (autoShow)
                        existing.Pane.Visible = true;
                    return existing.Control;
                }

                Office.CustomTaskPane pane = _ctpFactory.CreateCTP(
                    "MyanmarSpellChecker.ExcelTaskPaneControl",
                    "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (Excel)",
                    activeWin);

                var ctrl = (ExcelProofingTaskPaneControl)pane.ContentControl;
                if (_proofingEngine != null)
                {
                    ctrl.SetLexiconStats(
                        _proofingEngine.Trie.WordCount,
                        _proofingEngine.Trie.OfficialThatponCount,
                        _proofingEngine.Trie.RuleCount,
                        _proofingEngine.Trie.ReferenceCount,
                        _proofingEngine.Trie.EncyclopediaCount);
                    ctrl.SetStyleMode(_proofingEngine.StyleMode);
                }
                else
                {
                    ctrl.SetEngineWarmupState();
                    ctrl.SetStyleMode(_pendingStyleMode);
                }

                if (_scannerToolkit != null)
                {
                    ctrl.SetScanScope(_scannerToolkit.CurrentScope);
                    ctrl.SetUndoState(_scannerToolkit.UndoManager.UndoCount, _scannerToolkit.UndoManager.LatestDescription);
                }

                WireTaskPaneEvents(ctrl);

                pane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;
                pane.Width = 440;
                pane.VisibleStateChange += ctp =>
                {
                    try { _ribbonUi?.InvalidateControl("BtnToggleTaskPane"); } catch { }
                };
                if (autoShow)
                    pane.Visible = true;

                _windowTaskPanes[hwnd] = (pane, ctrl);
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
                return ctrl;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("GetOrCreateActiveWindowTaskPaneControl", ex);
                return null;
            }
        }

        private void ScheduleDeferredTaskPaneCreation()
        {
            if (_startupTaskPaneTimer != null)
                return;

            _startupTaskPaneRetries = 0;
            _startupTaskPaneTimer = new Timer { Interval = 300 };
            _startupTaskPaneTimer.Tick += (s, e) =>
            {
                _startupTaskPaneRetries++;
                Excel.Window? win = null;
                try { win = _excelApp?.ActiveWindow; } catch { }
                if ((win != null && win.Hwnd != 0) || _startupTaskPaneRetries >= 25)
                {
                    _startupTaskPaneTimer?.Stop();
                    _startupTaskPaneTimer?.Dispose();
                    _startupTaskPaneTimer = null;

                    if (win != null && win.Hwnd != 0)
                    {
                        GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                    }
                }
            };
            _startupTaskPaneTimer.Start();
        }

        private void WireTaskPaneEvents(ExcelProofingTaskPaneControl ctrl)
        {
            ctrl.RequestScanScope += scope => TriggerScanWithDbHotReload(scope);
            ctrl.RequestFixAllIssues += ExecuteFixAllInActiveScope;
            ctrl.RequestUndoLastAction += ExecuteUndoLastAction;
            ctrl.RequestClearUnderlines += () => ExecuteClearExcelUnderlines();
            ctrl.RequestLicenseDialog += ShowLicenseDialog;
            ctrl.RequestChangeStyleMode += ApplyStyleModeAndRescan;
            ctrl.RequestSelectIssueInSheet += item => _scannerToolkit?.SelectAndScrollToIssue(item);

            ctrl.RequestApplySuggestion += (item, replacement) =>
            {
                if (_scannerToolkit == null)
                    return;

                if (_scannerToolkit.ApplySingleFix(item, replacement, out string err))
                {
                    ctrl.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                    ctrl.ShowToastBanner($"✓ {item.DisplayLocation} အကွက်တွင် \"{replacement}\" ဖြင့် ပြင်ဆင်ပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။");
                }
                else if (!string.IsNullOrEmpty(err))
                {
                    ctrl.ShowToastBanner(err, isSuccess: false);
                }
            };

            ctrl.RequestApplyBatchSuggestion += (orig, rep) =>
            {
                if (_scannerToolkit == null)
                    return;

                int count = _scannerToolkit.ApplySameWordBatchFix(orig, rep, out string warn);
                ctrl.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                if (count > 0)
                {
                    ctrl.ShowToastBanner($"✓ တူညီသောအမှား ({count}) နေရာလုံးကို \"{rep}\" ဖြင့် တစ်ပြိုင်နက် ပြင်ဆင်ပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။");
                }
                else if (!string.IsNullOrEmpty(warn))
                {
                    ctrl.ShowToastBanner(warn, isSuccess: false);
                }
            };

            ctrl.RequestIgnoreOnce += item =>
            {
                _proofingEngine?.IgnoreWordInSession(item.Issue.OriginalText);
                item.IsResolved = true;
                if (_scannerToolkit != null)
                {
                    _scannerToolkit.RefreshVisualUnderlinesForActiveWorkbook();
                    ctrl.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                }
            };

            ctrl.RequestAddToDictionary += item =>
            {
                if (_proofingEngine == null)
                    return;

                _proofingEngine.AddCustomWord(item.Issue.OriginalText, _customDictFilePath);
                string normAdded = UnicodeNormalizer.NormalizeCanonicalOrder(item.Issue.OriginalText.Trim());
                if (_scannerToolkit != null)
                {
                    foreach (var cached in _scannerToolkit.GetCachedIssuesForActiveWorkbook())
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                        {
                            cached.IsResolved = true;
                        }
                    }
                    _scannerToolkit.RefreshVisualUnderlinesForActiveWorkbook();
                    ctrl.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                    ctrl.ShowToastBanner($"✓ \"{item.Issue.OriginalText}\" ကို ကိုယ်ပိုင်အဘိဓာန် (Word + Excel) သို့ မှတ်သားပြီးပါပြီ။");
                }
            };

            ctrl.RequestDictionarySearch += query =>
                _proofingEngine?.SearchDictionary(query, 45) ?? new List<DictionaryEntry>();
            ctrl.RequestSearchReferenceCorpus += (query, incProverbs, incEncyc) =>
                _proofingEngine?.SearchReferenceCorpus(query, incProverbs, incEncyc, 40) ?? new List<DictionaryEntry>();
            ctrl.RequestGetFeaturedKnowledge += () =>
                _proofingEngine?.GetFeaturedKnowledgeEntries(8) ?? new List<DictionaryEntry>();
            ctrl.RequestGetSynonyms += word =>
                _proofingEngine?.GetSynonyms(word, 8) ?? new List<string>();
            ctrl.RequestGetAntonyms += word =>
                _proofingEngine?.GetAntonyms(word, 8) ?? new List<string>();
            ctrl.RequestHomophoneSummary += word =>
                _proofingEngine?.GetHomophoneSiblingsSummary(word) ?? string.Empty;
            ctrl.RequestGetSelectedCellText += () =>
                _scannerToolkit?.TryGetSelectedCellText(80) ?? string.Empty;
            ctrl.RequestGetSelectedNumericText += () =>
                _scannerToolkit?.TryGetSelectedNumericText() ?? string.Empty;
            ctrl.RequestInsertTextIntoCell += text =>
            {
                if (_scannerToolkit == null)
                    return;
                bool ok = _scannerToolkit.TryInsertTextIntoActiveCell(text, out string msg);
                ctrl.ShowToastBanner(msg, ok);
            };

            ctrl.RequestGetCustomWords += () =>
                _proofingEngine?.GetCustomWords() ?? Array.Empty<string>();
            ctrl.RequestAddCustomWordManual += word =>
            {
                if (_proofingEngine == null || !_proofingEngine.AddCustomWord(word, _customDictFilePath))
                    return false;

                string normAdded = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(word.Trim()));
                if (_scannerToolkit != null)
                {
                    foreach (var cached in _scannerToolkit.GetCachedIssuesForActiveWorkbook())
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                            cached.IsResolved = true;
                    }
                    _scannerToolkit.RefreshVisualUnderlinesForActiveWorkbook();
                }
                return true;
            };
            ctrl.RequestRemoveCustomWord += word =>
            {
                if (_proofingEngine == null || !_proofingEngine.RemoveCustomWord(word, _customDictFilePath))
                    return false;

                if (_scannerToolkit != null)
                    _scannerToolkit.ScanScope(_scannerToolkit.CurrentScope);
                return true;
            };

            // Toolkit Actions
            ctrl.RequestConvertLegacyFonts += selectionOnly =>
            {
                if (_scannerToolkit == null) return;
                var res = _scannerToolkit.ConvertLegacyFontsInScope(selectionOnly);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestFormatNumerals += (toMyanmar, selectionOnly) =>
            {
                if (_scannerToolkit == null) return;
                var res = _scannerToolkit.FormatNumeralsInScope(toMyanmar, selectionOnly);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestRewriteToOfficialStyle += selectionOnly =>
            {
                if (_scannerToolkit == null) return;
                var res = _scannerToolkit.RewriteOfficialProseInScope(selectionOnly);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestFormatPunctuation += selectionOnly =>
            {
                if (_scannerToolkit == null) return;
                var res = _scannerToolkit.FormatPunctuationInScope(selectionOnly);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestRefreshQualityReport += () =>
                _scannerToolkit?.BuildActiveWorkbookQualityReport(forceRefresh: true) ?? new DocumentQualityReport();
            ctrl.RequestExportAuditWorkbook += () =>
            {
                if (_scannerToolkit == null) return;
                var rep = _scannerToolkit.BuildActiveWorkbookQualityReport(forceRefresh: true);
                bool ok = _scannerToolkit.GenerateAuditReportWorkbook(rep);
                ctrl.ShowToastBanner(
                    ok
                        ? "✓ မြန်မာစာ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (Excel Workbook အသစ်) ထုတ်ပေးပြီးပါပြီ။"
                        : "⚠ အစီရင်ခံစာ ထုတ်ယူစဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။",
                    ok);
            };
        }

        private void ExecuteFixAllInActiveScope(IssueFilterMode filterMode = IssueFilterMode.All)
        {
            if (_scannerToolkit == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            if (filterMode == IssueFilterMode.ContextOnly)
            {
                ctrl?.ShowToastBanner(
                    "⚠ အနက်သံသယ (Context Check) သတိပြုချက်များကို မှားယွင်းပြင်ဆင်မှု မဖြစ်စေရန် တစ်ခုချင်း စိစစ်ရွေးချယ်ပါ။",
                    isSuccess: false);
                return;
            }

            Func<SpreadsheetCellIssue, bool> predicate = filterMode switch
            {
                IssueFilterMode.SpellingOnly => i => !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning,
                IssueFilterMode.GrammarOnly => i => i.Issue.IsGrammarOrStyle,
                _ => i => !i.Issue.IsContextualWarning
            };

            string scopeLabel = filterMode switch
            {
                IssueFilterMode.SpellingOnly => "သတ်ပုံအမှား",
                IssueFilterMode.GrammarOnly => "သဒ္ဒါ/စာဟန်အမှား",
                _ => "အမှားစုစုပေါင်း"
            };

            var cachedBefore = _scannerToolkit.GetCachedIssuesForActiveWorkbook();
            int remainingContextChecks = cachedBefore.Count(i => !i.IsResolved && i.Issue.IsContextualWarning);

            int fixedCount = _scannerToolkit.ApplyBatchFixAll(predicate, $"{scopeLabel} အားလုံးပြင်", out string busyWarn);
            ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());

            if (!string.IsNullOrEmpty(busyWarn))
            {
                ctrl?.ShowToastBanner(busyWarn, isSuccess: false);
                return;
            }

            if (fixedCount > 0)
            {
                string ctxTail = (filterMode == IssueFilterMode.All && remainingContextChecks > 0)
                    ? $" (အနက်သံသယ {remainingContextChecks} ခုကို တစ်ခုချင်း စိစစ်ရန် ချန်လှပ်ထားသည်)"
                    : string.Empty;
                ctrl?.ShowToastBanner($"✓ {scopeLabel} ({fixedCount}) ခုကို Formula မပျက်စေဘဲ ပြင်ဆင်ပြီးပါပြီ{ctxTail} (↶ ပြန်ဆုတ်နိုင်သည်)။");
            }
            else if (remainingContextChecks > 0 && filterMode == IssueFilterMode.All)
            {
                ctrl?.ShowToastBanner($"⚠ အလိုအလျောက် ပြင်ဆင်နိုင်သော အမှား မကျန်ရှိပါ — အနက်သံသယ ({remainingContextChecks}) ခုကို တစ်ခုချင်း စိစစ်ပါ။", isSuccess: false);
            }
            else
            {
                ctrl?.ShowToastBanner("✓ ပြင်ဆင်ရန် အမှားအယွင်း မကျန်ရှိတော့ပါ။");
            }
        }

        private void ExecuteUndoLastAction()
        {
            if (_scannerToolkit == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            bool ok = _scannerToolkit.UndoLastAction(out string msg);
            ctrl?.ShowToastBanner(msg, ok);
        }

        private void TriggerScanWithDbHotReload(SpreadsheetScanScope scope)
        {
            if (_proofingEngine == null || _scannerToolkit == null)
                return;

            var activeCtrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            activeCtrl?.SetScanScope(scope);

            // Also re-sync shared custom_dictionary.txt in case user added words inside MS Word moments ago!
            if (!string.IsNullOrEmpty(_customDictFilePath) && File.Exists(_customDictFilePath))
            {
                _proofingEngine.LoadCustomDictionaryFile(_customDictFilePath);
            }

            string dbPath = _databaseFilePath;
            if (!string.IsNullOrWhiteSpace(dbPath) &&
                File.Exists(dbPath) &&
                File.GetLastWriteTimeUtc(dbPath) > _loadedDbLastWriteUtc &&
                System.Threading.Interlocked.CompareExchange(ref _dbHotReloadInProgress, 1, 0) == 0)
            {
                string keySnapshot = !string.IsNullOrWhiteSpace(_activeDatabaseKey)
                    ? _activeDatabaseKey
                    : AntiTamperGuard.ReconstructProtectedFallbackKey();
                string ministryDict = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MyanmarSpellChecker",
                    "ministry_custom_dictionary.txt");

                activeCtrl?.SetScanningState(true);

                Task.Run(() =>
                {
                    try
                    {
                        var repo = new EncryptedDictionaryRepository(dbPath);
                        MyanmarTrie freshTrie = repo.LoadIntoTrie(keySnapshot);
                        DateTime newStamp = File.GetLastWriteTimeUtc(dbPath);
                        freshTrie.PrewarmSearchIndexes();
                        _proofingEngine.HotSwapTrie(freshTrie);
                        _proofingEngine.LoadCustomDictionaryFile(ministryDict);
                        _proofingEngine.LoadCustomDictionaryFile(_customDictFilePath);

                        Control? marshaller = _uiMarshaller;
                        if (marshaller != null && marshaller.IsHandleCreated && !marshaller.IsDisposed)
                        {
                            marshaller.BeginInvoke(new Action(() =>
                            {
                                _loadedDbLastWriteUtc = newStamp;
                                foreach (var kvp in _windowTaskPanes.Values)
                                {
                                    kvp.Control.SetLexiconStats(
                                        freshTrie.WordCount,
                                        freshTrie.OfficialThatponCount,
                                        freshTrie.RuleCount,
                                        freshTrie.ReferenceCount,
                                        freshTrie.EncyclopediaCount);
                                }
                                _scannerToolkit.ScanScope(scope);
                            }));
                        }
                    }
                    catch
                    {
                        Control? marshaller = _uiMarshaller;
                        if (marshaller != null && marshaller.IsHandleCreated && !marshaller.IsDisposed)
                        {
                            marshaller.BeginInvoke(new Action(() => _scannerToolkit?.ScanScope(scope)));
                        }
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _dbHotReloadInProgress, 0);
                    }
                });
                return;
            }

            _scannerToolkit.ScanScope(scope);
        }

        private bool LoadSavedVisualUnderlineSetting()
        {
            try
            {
                if (!string.IsNullOrEmpty(_visualUnderlineConfigPath) && File.Exists(_visualUnderlineConfigPath))
                {
                    string raw = File.ReadAllText(_visualUnderlineConfigPath).Trim();
                    if (bool.TryParse(raw, out bool parsed))
                        return parsed;
                }
            }
            catch { }
            return true;
        }

        private void ExecuteClearExcelUnderlines()
        {
            if (_scannerToolkit == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            int cleaned = _scannerToolkit.ClearAllVisualUnderlinesInActiveWorkbook();
            ctrl?.ShowToastBanner(
                cleaned > 0
                    ? $"✓ အကွက် ({cleaned}) ကွက်ရှိ စစ်ဆေးရေး အနီ/အပြာ မျဉ်းသားချက်များကို ဖယ်ရှားပြီးပါပြီ။"
                    : "✓ ဖယ်ရှားရန် စစ်ဆေးရေးမျဉ်း မရှိပါ။");
        }

        private ProofingStyleMode LoadSavedStyleMode()
        {
            try
            {
                if (!string.IsNullOrEmpty(_styleModeConfigPath) && File.Exists(_styleModeConfigPath))
                {
                    string raw = File.ReadAllText(_styleModeConfigPath).Trim();
                    if (Enum.TryParse(raw, ignoreCase: true, out ProofingStyleMode parsed))
                        return parsed;
                }
            }
            catch { }
            return ProofingStyleMode.GeneralStandard;
        }

        private void ApplyStyleModeAndRescan(ProofingStyleMode mode)
        {
            _pendingStyleMode = mode;
            try
            {
                if (!string.IsNullOrEmpty(_styleModeConfigPath))
                    File.WriteAllText(_styleModeConfigPath, mode.ToString());
            }
            catch { }

            foreach (var kv in _windowTaskPanes.Values)
            {
                kv.Control.SetStyleMode(mode);
            }
            _ribbonUi?.InvalidateControl("BtnToggleFormalStyle");

            if (_proofingEngine == null || _scannerToolkit == null)
                return;

            _proofingEngine.StyleMode = mode;
            _scannerToolkit.ScanScope(_scannerToolkit.CurrentScope);
        }

        private static void SuppressOfficeMissingBurmeseBannerRegistry()
        {
            foreach (string ver in new[] { "16.0", "15.0" })
            {
                try
                {
                    string subKeyPath = $@"Software\Microsoft\Office\{ver}\Common\LanguageResources";
                    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true))
                    {
                        if (key == null)
                            continue;

                        string existing = (key.GetValue("NotificationsNeverShowAgainLanguages") as string) ?? string.Empty;
                        var parts = existing
                            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .ToList();

                        bool changed = false;
                        foreach (string tag in new[] { "my-MM", "my" })
                        {
                            if (!parts.Any(p => string.Equals(p, tag, StringComparison.OrdinalIgnoreCase)))
                            {
                                parts.Add(tag);
                                changed = true;
                            }
                        }

                        if (changed)
                        {
                            key.SetValue("NotificationsNeverShowAgainLanguages", string.Join(";", parts), RegistryValueKind.String);
                        }
                    }
                }
                catch { }
            }
        }

        // ================= EXCEL RIBBON UI =================

        public string GetCustomUI(string ribbonId)
        {
            if (!_isPrimaryInstance)
                return string.Empty;

            return @"
<customUI xmlns='http://schemas.microsoft.com/office/2009/07/customui' onLoad='OnRibbonLoad'>
  <ribbon>
    <tabs>
      <tab id='TabMyanmarExcelProofing' label='မြန်မာစာ စစ်ဆေးရေး'>
        <group id='GrpExcelScanAndFix' label='ဇယားကွက် စစ်ဆေးရေး (Formula-Safe)'>
          <button id='BtnScanActiveSheet'
                  label='လက်ရှိ Sheet စစ်မည်'
                  size='large'
                  imageMso='SpellingAndGrammar'
                  onAction='OnRibbonScanSheet'
                  screentip='လက်ရှိ Worksheet ရှိ မြန်မာစာ အကွက်အားလုံးကို Formula မထိခိုက်စေဘဲ အမြန်စစ်ဆေးရန်' />
          <button id='BtnScanSelection'
                  label='ရွေးကွက် စစ်မည်'
                  size='normal'
                  imageMso='SelectCell'
                  onAction='OnRibbonScanSelection'
                  screentip='ရွေးချယ်ထားသော အကွက်များ (Selected Range) ကိုသာ စစ်ဆေးရန်' />
          <button id='BtnScanWorkbook'
                  label='Workbook လုံး စစ်မည်'
                  size='normal'
                  imageMso='WorkbookViewsCustom'
                  onAction='OnRibbonScanWorkbook'
                  screentip='Excel စာအုပ်တစ်ခုလုံးရှိ Sheet အားလုံးကို စစ်ဆေးရန်' />
          <button id='BtnFixAllIssues'
                  label='အားလုံး တစ်ပြိုင်နက် ပြင်မည်'
                  size='large'
                  imageMso='AcceptInvitation'
                  onAction='OnRibbonFixAllIssues'
                  screentip='သေချာသော သတ်ပုံနှင့် သဒ္ဒါအမှားအားလုံးကို Formula မထိခိုက်ဘဲ တစ်ပြိုင်နက် ပြင်ဆင်ရန်' />
          <button id='BtnUndoLastFix'
                  label='ပြန်ဆုတ်မည် (Undo)'
                  size='normal'
                  imageMso='Undo'
                  getEnabled='GetUndoEnabled'
                  onAction='OnRibbonUndoLastFix'
                  screentip='နောက်ဆုံး ပြင်ဆင်ခဲ့သော အကွက်များကို မူလအတိုင်း ပြန်ဆုတ်ရန်' />
        </group>
        <group id='GrpExcelStyleAndLive' label='အရေးအသားနှင့် အလိုအလျောက်စနစ်'>
          <toggleButton id='BtnToggleFormalStyle'
                        label='စာပေ / ရုံးသုံး အရေးစကားစနစ်'
                        size='large'
                        imageMso='FileDocumentInspect'
                        getPressed='GetFormalStylePressed'
                        onAction='OnRibbonToggleFormalStyle'
                        screentip='ရုံးသုံးနှင့် စာပေအရေးအသား (ပါတယ်➔ပါသည်၊ တွေ➔များ) စစ်ဆေးမှုကို ဖွင့်/ပိတ်ရန်' />
          <toggleButton id='BtnToggleLiveScan'
                        label='အလိုအလျောက် စစ်ဆေးမှု (Live)'
                        size='large'
                        imageMso='ReviewTrackChanges'
                        getPressed='GetLiveScanPressed'
                        onAction='OnRibbonToggleLiveScan'
                        screentip='အကွက်အတွင်း စာရိုက်ပြီး Enter နှိပ်တိုင်း အလိုအလျောက် စစ်ဆေးပေးရန်' />
          <toggleButton id='BtnToggleVisualUnderlines'
                        label='အနီမျဉ်း ပြသမှု (Underlines)'
                        size='normal'
                        imageMso='FontColorPicker'
                        getPressed='GetVisualUnderlinesPressed'
                        onAction='OnRibbonToggleVisualUnderlines'
                        screentip='အမှားရှိသော စာသားကွက်များတွင် အနီ/အပြာ/လိမ္မော် မျဉ်းသားပြသမှုကို ဖွင့်/ပိတ်ရန် (Save/Print လုပ်ချိန် အလိုအလျောက် ဖယ်ရှားပေးသည်)' />
          <button id='BtnClearExcelUnderlines'
                  label='မျဉ်းရှင်းမည်'
                  size='normal'
                  imageMso='ClearFormatting'
                  onAction='OnRibbonClearUnderlines'
                  screentip='Excel အကွက်များအတွင်းရှိ စစ်ဆေးရေး မျဉ်းသားချက်အားလုံးကို ချက်ချင်းဖယ်ရှားရန်' />
        </group>
        <group id='GrpExcelDictionary' label='အဘိဓာန်နှင့် စစ်ဆေးရေးပြကွက် (Offline)'>
          <button id='BtnSearchDictionary'
                  label='သတ်ပုံကျမ်းနှင့် အဘိဓာန် ရှာရန်'
                  size='large'
                  imageMso='LookUpReference'
                  onAction='OnRibbonSearchDictionary'
                  screentip='သတ်ပုံကျမ်း၊ မြန်မာ-မြန်မာ အဘိဓာန် နှင့် စွယ်စုံဗဟုသုတ (၂၅,၀၀၀+) ရှာဖွေရန်' />
          <toggleButton id='BtnToggleTaskPane'
                        label='စစ်ဆေးရေးပြကွက် ပြ/ဖျောက်'
                        size='large'
                        imageMso='ResearchPane'
                        getPressed='GetTaskPanePressed'
                        onAction='OnRibbonToggleTaskPane'
                        screentip='ညာဘက် စစ်ဆေးရေးပြကွက် (Task Pane) ကို ဖွင့်/ပိတ် ပြုလုပ်ရန်' />
          <button id='BtnManageCustomWords'
                  label='မှတ်သားဝေါဟာရ စီမံရန်'
                  size='normal'
                  imageMso='BookmarkInsert'
                  onAction='OnRibbonManageCustomWords'
                  screentip='Word နှင့် Excel မျှဝေသုံးစွဲသော ကိုယ်ပိုင်ဝေါဟာရများ စီမံရန်' />
          <button id='BtnLicenseActivation'
                  label='ဖန်တီးသူနှင့် လိုင်စင် (GPLv3)'
                  size='normal'
                  imageMso='Info'
                  onAction='OnRibbonOpenLicense'
                  screentip='စနစ်အချက်အလက်၊ ဖန်တီးသူ ထက်ရာဇာ (ကျောက်ဆည်တက္ကသိုလ်) နှင့် GNU GPL v3.0 Open-Source လိုင်စင် ကြည့်ရှုရန်' />
        </group>
        <group id='GrpExcelTypography' label='ဖောင့်နှင့် ကိန်းဂဏန်း ကိရိယာ'>
          <button id='BtnConvertFonts'
                  label='WinInnwa/Zawgyi ➔ Pyidaungsu'
                  size='large'
                  imageMso='FontDialog'
                  onAction='OnRibbonConvertFonts'
                  screentip='Formula နှင့် ဂဏန်းကွက်များကို မထိခိုက်ဘဲ Pyidaungsu Unicode သို့ ပြောင်းလဲရန်' />
          <button id='BtnNumeralsToMyanmar'
                  label='0-9 ➔ ၀-၉ (စာသားကွက်)'
                  size='normal'
                  imageMso='BulletsAndNumberingDialog'
                  onAction='OnRibbonNumeralsToMyanmar'
                  screentip='တွက်ချက်မှု ကိန်းဂဏန်းကွက်နှင့် Formula များကို မထိခိုက်ဘဲ စာသားကွက်ရှိ ဂဏန်းများ ပြောင်းရန်' />
          <button id='BtnCleanPunctuation'
                  label='ပုဒ်ဖြတ် ၊ / ။ ရှင်းမည်'
                  size='normal'
                  imageMso='ParagraphMarks'
                  onAction='OnRibbonCleanPunctuation'
                  screentip='ပုဒ်ထီး (၊)၊ ပုဒ်မ (။) အကွာအဝေးနှင့် ZWSP များကို သန့်စင်ရန်' />
        </group>
        <group id='GrpExcelAccounting' label='ဘဏ္ဍာရေးနှင့် အရည်အသွေး'>
          <button id='BtnOpenToolkitPane'
                  label='ကျပ်သိန်းနှင့် ပြက္ခဒိန်'
                  size='large'
                  imageMso='DateAndTimeInsert'
                  onAction='OnRibbonOpenToolkitPane'
                  screentip='ကျပ်ငွေ (သိန်း) စာသားပြောင်းစက်၊ မြန်မာသက္ကရာဇ် ပြက္ခဒိန် နှင့် ဇယားကွက် ကိရိယာများ ဖွင့်ရန်' />
          <button id='BtnRewriteOfficialProse'
                  label='ရုံးသုံးစာပေဟန် ပြောင်းမည်'
                  size='normal'
                  imageMso='Translate'
                  onAction='OnRibbonRewriteOfficialProse'
                  screentip='စကားပြောဟန် အသုံးအနှုန်းများကို ရုံးသုံးစာပေဟန်သို့ ပြောင်းလဲရန်' />
          <button id='BtnExportAuditReport'
                  label='အရည်အသွေး အစီရင်ခံစာ'
                  size='normal'
                  imageMso='FilePrintPreview'
                  onAction='OnRibbonExportAuditReport'
                  screentip='Excel မြန်မာစာ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ ထုတ်ယူရန်' />
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>";
        }

        public void OnRibbonLoad(Office.IRibbonUI ribbonUi)
        {
            _ribbonUi = ribbonUi;
        }

        public bool GetUndoEnabled(Office.IRibbonControl control)
        {
            return _scannerToolkit?.UndoManager.CanUndo == true;
        }

        public bool GetFormalStylePressed(Office.IRibbonControl control)
        {
            return (_proofingEngine?.StyleMode ?? _pendingStyleMode) == ProofingStyleMode.FormalOfficial;
        }

        public void OnRibbonToggleFormalStyle(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleFormalStyle", () =>
            {
                ApplyStyleModeAndRescan(pressed ? ProofingStyleMode.FormalOfficial : ProofingStyleMode.GeneralStandard);
            });
        }

        public bool GetLiveScanPressed(Office.IRibbonControl control)
        {
            return _scannerToolkit?.IsLiveAutoScanEnabled ?? _pendingLiveAutoScan;
        }

        public void OnRibbonToggleLiveScan(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleLiveScan", () =>
            {
                _pendingLiveAutoScan = pressed;
                if (_scannerToolkit != null)
                    _scannerToolkit.IsLiveAutoScanEnabled = pressed;
                _ribbonUi?.InvalidateControl("BtnToggleLiveScan");
            });
        }

        public bool GetVisualUnderlinesPressed(Office.IRibbonControl control)
        {
            return _scannerToolkit?.IsVisualUnderlineEnabled ?? _pendingVisualUnderlines;
        }

        public void OnRibbonToggleVisualUnderlines(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleVisualUnderlines", () =>
            {
                _pendingVisualUnderlines = pressed;
                try
                {
                    if (!string.IsNullOrEmpty(_visualUnderlineConfigPath))
                        File.WriteAllText(_visualUnderlineConfigPath, pressed.ToString());
                }
                catch { }

                if (_scannerToolkit != null)
                {
                    _scannerToolkit.IsVisualUnderlineEnabled = pressed;
                    _scannerToolkit.RefreshVisualUnderlinesForActiveWorkbook();
                }

                _ribbonUi?.InvalidateControl("BtnToggleVisualUnderlines");
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.ShowToastBanner(
                    pressed
                        ? "✓ Excel အကွက်အတွင်း အနီ/အပြာ/လိမ္မော် စစ်ဆေးရေးမျဉ်း ပြသမှုကို ဖွင့်လိုက်ပါပြီ (Save/Print လုပ်ချိန် အလိုအလျောက် ဖယ်ရှားပေးသည်)။"
                        : "✓ Excel အကွက်အတွင်း မျဉ်းသားပြသမှုကို ပိတ်ပြီး မျဉ်းအားလုံး ရှင်းလင်းလိုက်ပါပြီ။");
            });
        }

        public void OnRibbonClearUnderlines(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonClearUnderlines", ExecuteClearExcelUnderlines);
        }

        public bool GetTaskPanePressed(Office.IRibbonControl control)
        {
            if (_excelApp == null)
                return false;

            int hwnd = 0;
            try { hwnd = _excelApp.ActiveWindow?.Hwnd ?? 0; } catch { }
            return hwnd != 0 && _windowTaskPanes.TryGetValue(hwnd, out var entry) && entry.Pane.Visible;
        }

        public void OnRibbonScanSheet(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonScanSheet", () => TriggerScanWithDbHotReload(SpreadsheetScanScope.ActiveSheet));
        }

        public void OnRibbonScanSelection(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonScanSelection", () => TriggerScanWithDbHotReload(SpreadsheetScanScope.Selection));
        }

        public void OnRibbonScanWorkbook(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonScanWorkbook", () => TriggerScanWithDbHotReload(SpreadsheetScanScope.EntireWorkbook));
        }

        public void OnRibbonFixAllIssues(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonFixAllIssues", () => ExecuteFixAllInActiveScope(IssueFilterMode.All));
        }

        public void OnRibbonUndoLastFix(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonUndoLastFix", ExecuteUndoLastAction);
        }

        public void OnRibbonSearchDictionary(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonSearchDictionary", () =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                string sel = _scannerToolkit?.TryGetSelectedCellText(40) ?? string.Empty;
                ctrl?.OpenDictionaryWithQuery(sel);
            });
        }

        public void OnRibbonToggleTaskPane(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleTaskPane", () =>
            {
                if (_excelApp == null) return;
                int hwnd = 0;
                try { hwnd = _excelApp.ActiveWindow?.Hwnd ?? 0; } catch { }

                if (_windowTaskPanes.TryGetValue(hwnd, out var entry))
                {
                    entry.Pane.Visible = pressed;
                }
                else if (pressed)
                {
                    GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                }
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
            });
        }

        public void OnRibbonManageCustomWords(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonManageCustomWords", () =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                ctrl?.OpenCustomWordsTab();
            });
        }

        public void OnRibbonOpenLicense(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonOpenLicense", ShowLicenseDialog);
        }

        public void OnRibbonConvertFonts(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonConvertFonts", () =>
            {
                if (_scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.ConvertLegacyFontsInScope(selectionOnly: false);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonNumeralsToMyanmar(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonNumeralsToMyanmar", () =>
            {
                if (_scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.FormatNumeralsInScope(toMyanmarDigits: true, selectionOnly: false);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonCleanPunctuation(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonCleanPunctuation", () =>
            {
                if (_scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.FormatPunctuationInScope(selectionOnly: false);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonOpenToolkitPane(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonOpenToolkitPane", () =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                ctrl?.OpenToolkitTab();
            });
        }

        public void OnRibbonRewriteOfficialProse(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonRewriteOfficialProse", () =>
            {
                if (_scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.RewriteOfficialProseInScope(selectionOnly: false);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonExportAuditReport(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonExportAuditReport", () =>
            {
                if (_scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var rep = _scannerToolkit.BuildActiveWorkbookQualityReport(forceRefresh: true);
                bool ok = _scannerToolkit.GenerateAuditReportWorkbook(rep);
                ctrl?.ShowToastBanner(
                    ok
                        ? "✓ မြန်မာစာ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (Excel Workbook အသစ်) ထုတ်ပေးပြီးပါပြီ။"
                        : "⚠ အစီရင်ခံစာ ထုတ်ယူစဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။",
                    ok);
            });
        }

        private void ShowLicenseDialog()
        {
            int totalEntries = (_proofingEngine?.Trie.WordCount ?? 0) + (_proofingEngine?.Trie.ReferenceCount ?? 0);
            using (var dlg = new LicenseActivationForm(DefaultVendorPublicKeyXml, _licenseFilePath, totalEntries))
            {
                dlg.ShowDialog();
            }
        }

        public void OnDisconnection(ext_DisconnectMode removeMode, ref Array custom)
        {
            try
            {
                if (_isPrimaryInstance)
                {
                    lock (s_instanceGate)
                    {
                        s_primaryInstanceActive = false;
                    }
                    _isPrimaryInstance = false;
                }

                _startupTaskPaneTimer?.Stop();
                _startupTaskPaneTimer?.Dispose();
                _startupTaskPaneTimer = null;

                if (_excelApp != null)
                {
                    _excelApp.SheetChange -= OnSheetChange;
                    _excelApp.SheetActivate -= OnSheetActivate;
                    _excelApp.WindowActivate -= OnWindowActivate;
                    _excelApp.WorkbookOpen -= OnWorkbookOpen;
                    ((Excel.AppEvents_Event)_excelApp).NewWorkbook -= OnNewWorkbook;
                    _excelApp.WorkbookBeforeSave -= OnWorkbookBeforeSave;
                    _excelApp.WorkbookBeforePrint -= OnWorkbookBeforePrint;
                    _excelApp.WorkbookBeforeClose -= OnWorkbookBeforeClose;
                }

                _scannerToolkit?.Dispose();
                _scannerToolkit = null;

                _windowTaskPanes.Clear();
                _uiMarshaller?.Dispose();
                _uiMarshaller = null;
            }
            catch
            {
                // Ignore
            }
        }

        public void OnAddInsUpdate(ref Array custom) { }
        public void OnStartupComplete(ref Array custom) { }
        public void OnBeginShutdown(ref Array custom) { }
    }
}

