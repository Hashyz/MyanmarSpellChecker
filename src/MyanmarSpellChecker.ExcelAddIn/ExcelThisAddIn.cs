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
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Performance;
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

        public const string DefaultVendorPublicKeyXml = LicenseService.DefaultEmbeddedPublicKeyXml;

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
        private ExcelQuickFixController? _quickFixController;
        private ExcelWavyUnderlineOverlay? _wavyOverlay;
        private ProofingStyleMode _pendingStyleMode = ProofingStyleMode.GeneralStandard;
        private bool _pendingLiveAutoScan = true;
        private bool _pendingVisualUnderlines = true;

        // Per-Window Task Pane map (keyed by Excel.Window.Hwnd) for Multi-Workbook SDI support
        private readonly Dictionary<int, (Office.CustomTaskPane Pane, ExcelProofingTaskPaneControl Control)> _windowTaskPanes =
            new Dictionary<int, (Office.CustomTaskPane Pane, ExcelProofingTaskPaneControl Control)>();
        private readonly HashSet<int> _settledPaneWidthHwnds = new HashSet<int>();
        private readonly Dictionary<int, Timer> _paneWidthTimers = new Dictionary<int, Timer>();

        private string _appDataFolder = string.Empty;
        private string _uiLanguageConfigPath = string.Empty;
        private string _styleModeConfigPath = string.Empty;
        private string _ecoModeConfigPath = string.Empty;
        private string _visualUnderlineConfigPath = string.Empty;
        private string _customDictFilePath = string.Empty;
        private string _licenseFilePath = string.Empty;
        private string _databaseFilePath = string.Empty;
        private string _activeDatabaseKey = string.Empty;
        private DateTime _loadedDbLastWriteUtc = DateTime.MinValue;
        private int _dbHotReloadInProgress;
        private FixAllPreviewDialog<SpreadsheetCellIssue>? _activeReviewDialog;
        private string _activeReviewWorkbookName = string.Empty;
        private IssueFilterMode _activeReviewFilterMode = IssueFilterMode.All;
        private readonly Dictionary<string, List<ProofingSessionFixedRecord>> _sessionFixedByWorkbook =
            new Dictionary<string, List<ProofingSessionFixedRecord>>(StringComparer.OrdinalIgnoreCase);

        static ExcelThisAddIn()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var asmName = new System.Reflection.AssemblyName(args.Name);
                    foreach (string dir in GetCandidateAddInDirectories(typeof(ExcelThisAddIn).Assembly))
                    {
                        string candidatePath = Path.Combine(dir, asmName.Name + ".dll");
                        if (File.Exists(candidatePath))
                        {
                            return System.Reflection.Assembly.LoadFrom(candidatePath);
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
                return null;
            };
        }

        private static List<string> GetCandidateAddInDirectories(System.Reflection.Assembly asm)
        {
            var dirs = new List<string>(4);
            try
            {
                string? loc = asm.Location;
                if (!string.IsNullOrWhiteSpace(loc))
                {
                    string? locDir = Path.GetDirectoryName(loc);
                    if (!string.IsNullOrWhiteSpace(locDir) && Directory.Exists(locDir))
                        dirs.Add(locDir);
                }
            }
            catch { }

            try
            {
                string? codeBase = asm.CodeBase;
                if (!string.IsNullOrWhiteSpace(codeBase))
                {
                    string localPath = new Uri(codeBase).LocalPath;
                    string? cbDir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrWhiteSpace(cbDir) && Directory.Exists(cbDir) &&
                        !dirs.Contains(cbDir, StringComparer.OrdinalIgnoreCase))
                    {
                        dirs.Add(cbDir);
                    }
                }
            }
            catch { }

            try
            {
                string perUserAppDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyanmarSpellChecker",
                    "app");
                if (Directory.Exists(perUserAppDir) && !dirs.Contains(perUserAppDir, StringComparer.OrdinalIgnoreCase))
                    dirs.Add(perUserAppDir);
            }
            catch { }

            return dirs;
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
            List<string> addInDirs = GetCandidateAddInDirectories(typeof(ExcelThisAddIn).Assembly);
            string? installDir = addInDirs.Count > 0 ? addInDirs[0] : Path.GetDirectoryName(typeof(ExcelThisAddIn).Assembly.Location);
            string programDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyanmarSpellChecker");
            string localAppDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyanmarSpellChecker");
            string localAppSubDir = Path.Combine(localAppDataDir, "app");
            string progFilesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MyanmarSpellChecker");
            string progFilesX86Dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "MyanmarSpellChecker");

            _appDataFolder = Directory.Exists(programDataDir) ? programDataDir : localAppDataDir;
            Directory.CreateDirectory(_appDataFolder);
            Directory.CreateDirectory(localAppDataDir);
            AddInDiagnostics.ConfigureLogDirectory(localAppDataDir);

            _uiLanguageConfigPath = Path.Combine(localAppDataDir, "ui_language.txt");
            UiLocalizer.CurrentLanguage = UiLocalizer.LoadSavedLanguage(_uiLanguageConfigPath);
            _styleModeConfigPath = Path.Combine(localAppDataDir, "style_mode.txt");
            _ecoModeConfigPath = Path.Combine(localAppDataDir, "eco_mode.txt");
            EcoPerformanceGovernor.CurrentPreference = EcoPerformanceGovernor.LoadSavedPreference(_ecoModeConfigPath);
            _visualUnderlineConfigPath = Path.Combine(localAppDataDir, "excel_underlines.txt");
            _customDictFilePath = Path.Combine(localAppDataDir, "custom_dictionary.txt");
            string ministryCustomDictPath = Path.Combine(programDataDir, "ministry_custom_dictionary.txt");

            string installLic = !string.IsNullOrEmpty(installDir) ? Path.Combine(installDir, "license.lic") : string.Empty;
            string progDataLic = Path.Combine(programDataDir, "license.lic");
            _licenseFilePath = File.Exists(installLic)
                ? installLic
                : (File.Exists(progDataLic) ? progDataLic : Path.Combine(_appDataFolder, "license.lic"));

            var rawDbPaths = new List<string>(12);
            foreach (string dir in addInDirs)
            {
                rawDbPaths.Add(Path.Combine(dir, "mlc_thatpon.db"));
                string ancestorDb = FindAncestorRepoDatabasePath(dir);
                if (!string.IsNullOrEmpty(ancestorDb))
                    rawDbPaths.Add(ancestorDb);
            }
            rawDbPaths.Add(Path.Combine(localAppSubDir, "mlc_thatpon.db"));
            rawDbPaths.Add(Path.Combine(localAppDataDir, "mlc_thatpon.db"));
            rawDbPaths.Add(Path.Combine(programDataDir, "mlc_thatpon.db"));
            rawDbPaths.Add(Path.Combine(progFilesDir, "mlc_thatpon.db"));
            rawDbPaths.Add(Path.Combine(progFilesX86Dir, "mlc_thatpon.db"));
            string localAppDb = Path.Combine(localAppDataDir, "mlc_thatpon.db");

            // Always prefer the full compiled dictionary (>= 5 MB, 89,500+ headwords) over any small 106-word seed fallback DB
            var candidateDbs = rawDbPaths
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Select(p => Path.GetFullPath(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.Length >= 5_000_000 ? 2 : (fi.Length >= 100_000 ? 1 : 0))
                .ThenByDescending(fi => fi.LastWriteTimeUtc)
                .ThenByDescending(fi => fi.Length)
                .ToList();

            _databaseFilePath = candidateDbs.Count > 0
                ? candidateDbs[0].FullName
                : localAppDb;

            _pendingStyleMode = LoadSavedStyleMode();
            _pendingVisualUnderlines = LoadSavedVisualUnderlineSetting();

            string dbPathSnapshot = _databaseFilePath;
            string localAppDbSnapshot = localAppDb;
            string licPathSnapshot = _licenseFilePath;
            string userDictSnapshot = _customDictFilePath;
            ProofingStyleMode initialMode = _pendingStyleMode;

            // Decrypt mlc_thatpon.db, build 89,500+ headword Trie, and pre-JIT proofing hot path completely off Excel's UI thread!
            Task.Run(() =>
            {
                AddInDiagnostics.SafeExecute("AsyncEngineWarmup", () =>
                {
                    SuppressOfficeMissingBurmeseBannerRegistry();

                    // Self-heal %LocalAppData%\MyanmarSpellChecker\mlc_thatpon.db if an older build created a tiny 106-word seed DB
                    try
                    {
                        if (File.Exists(dbPathSnapshot) && new FileInfo(dbPathSnapshot).Length >= 5_000_000 &&
                            !string.Equals(Path.GetFullPath(dbPathSnapshot), Path.GetFullPath(localAppDbSnapshot), StringComparison.OrdinalIgnoreCase) &&
                            (!File.Exists(localAppDbSnapshot) || new FileInfo(localAppDbSnapshot).Length < 5_000_000))
                        {
                            File.Copy(dbPathSnapshot, localAppDbSnapshot, overwrite: true);
                        }
                    }
                    catch { }

                    string activeDbKey = AntiTamperGuard.ReconstructProtectedFallbackKey();
                    if (File.Exists(licPathSnapshot))
                    {
                        string licJson = File.ReadAllText(licPathSnapshot);
                        string pubKeyXml = LicenseService.ResolvePublicKeyXml(licPathSnapshot, DefaultVendorPublicKeyXml);
                        var licResult = LicenseService.ValidateLicenseJson(licJson, pubKeyXml);
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

                    // Lightweight 1-sentence background JIT warmup (~70ms) pre-compiles both valid & suggestion paths off the UI thread!
                    _ = engine.CheckText("မြန်မာစာ ပီးပီ။");

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

        private static string FindAncestorRepoDatabasePath(string? startDir)
        {
            try
            {
                string? cur = startDir;
                for (int depth = 0; depth <= 8 && !string.IsNullOrEmpty(cur); depth++)
                {
                    string candidate = Path.Combine(cur, "data", "mlc_thatpon.db");
                    if (File.Exists(candidate))
                        return candidate;
                    cur = Directory.GetParent(cur)?.FullName;
                }
            }
            catch { }
            return string.Empty;
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

            _quickFixController = new ExcelQuickFixController(
                _excelApp,
                _scannerToolkit,
                ApplySingleIssueFixFromUi,
                ApplySameWordBatchFixFromUi,
                IgnoreIssueFromUi,
                AddIssueWordToDictionaryFromUi,
                item => GetOrCreateActiveWindowTaskPaneControl(autoShow: false)?.HighlightCellIssueFromSheet(item),
                () => _wavyOverlay?.NotifyPopupBoundsChanged());

            _wavyOverlay = new ExcelWavyUnderlineOverlay(
                _excelApp,
                _scannerToolkit,
                () => _quickFixController?.ActivePopupHwnd ?? IntPtr.Zero,
                () => _quickFixController?.ActivePopupScreenBounds);
            IntPtr _ = _wavyOverlay.Handle;
            _wavyOverlay.ViewportScrolledOrResized += () => _quickFixController?.CloseActivePopup();
            _wavyOverlay.ExcelDeactivatedOrEditing += () => _quickFixController?.CloseActivePopup();

            _scannerToolkit.ScanStarted += () =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.SetScanningState(true);
            };
            _scannerToolkit.ScanProgressChanged += (pct, msg) =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.UpdateScanProgress(pct, msg);
            };
            _scannerToolkit.EcoActiveChanged += ecoActive => SyncEcoStateAcrossTaskPanes(ecoActive);
            _scannerToolkit.ScanCompleted += items =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                if (ctrl != null)
                {
                    ctrl.SetScanOutcomeMetadata(_scannerToolkit.GetLastScanMetadataForActiveWorkbook());
                    ctrl.RenderIssues(items);
                }
                if (_activeReviewDialog != null && !_activeReviewDialog.IsDisposed)
                {
                    try
                    {
                        var freshEntries = BuildExcelFixPreviewEntries(_activeReviewFilterMode);
                        _activeReviewDialog.ReloadEntriesFromHost(freshEntries);
                    }
                    catch { }
                }
                _quickFixController?.ScheduleRefreshForActiveCell(70);
            };
            _scannerToolkit.UndoStateChanged += SyncUndoStateAcrossTaskPanes;
            _scannerToolkit.RefreshEcoActiveState();

            foreach (var kvp in _windowTaskPanes.Values)
            {
                kvp.Control.SetLexiconStats(
                    _proofingEngine.Trie.WordCount,
                    _proofingEngine.Trie.OfficialThatponCount,
                    _proofingEngine.Trie.RuleCount,
                    _proofingEngine.Trie.ReferenceCount,
                    _proofingEngine.Trie.EncyclopediaCount);
                kvp.Control.SetStyleMode(_proofingEngine.StyleMode);
                kvp.Control.SetEcoModeState(EcoPerformanceGovernor.CurrentPreference, _scannerToolkit.IsEcoActive);
                kvp.Control.RefreshActiveTabAfterEngineReady();
            }

            _ribbonUi?.Invalidate();

            // Gentle idle background prewarm on 64-bit non-low-spec hosts so the very first Dictionary search
            // responds in < 1 ms (32-bit Office & Potato PCs keep 0 MB startup overhead until Dictionary is opened).
            var engineToPrewarm = _proofingEngine;
            if (engineToPrewarm != null &&
                Environment.Is64BitProcess &&
                !EcoPerformanceGovernor.Hardware.IsLowSpecHardware &&
                EcoPerformanceGovernor.CurrentPreference != EcoModePreference.AlwaysOn)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1800).ConfigureAwait(false);
                        if (ReferenceEquals(_proofingEngine, engineToPrewarm) &&
                            EcoPerformanceGovernor.CurrentPreference != EcoModePreference.AlwaysOn)
                        {
                            engineToPrewarm.PrewarmDictionaryIndexes();
                        }
                    }
                    catch { }
                });
            }

            // Perform gentle non-blocking streaming full-sheet scan if a workbook is already open (never blocks startup!)
            try
            {
                if (_excelApp.ActiveWorkbook != null && ExcelWorkbookScannerAndToolkit.IsExcelReadyForInterop(_excelApp))
                {
                    _scannerToolkit.ScheduleDeferredScanScope(SpreadsheetScanScope.ActiveSheet, 250, allowSmartPartialAutoScan: false);
                }
            }
            catch { }
        }

        private void HookExcelEvents()
        {
            if (_excelApp == null)
                return;

            _excelApp.SheetChange += OnSheetChange;
            _excelApp.SheetSelectionChange += OnSheetSelectionChange;
            _excelApp.SheetBeforeRightClick += OnSheetBeforeRightClick;
            _excelApp.SheetBeforeDoubleClick += OnSheetBeforeDoubleClick;
            _excelApp.SheetActivate += OnSheetActivate;
            _excelApp.WindowActivate += OnWindowActivate;
            _excelApp.WindowDeactivate += OnWindowDeactivate;
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
                // Screen-only overlay never touches .xlsx formatting; this also heals any legacy pre-overlay marks once
                _scannerToolkit?.TemporarilyStripUnderlinesForSaveOrPrint(wb);
            });
        }

        private void OnWorkbookBeforePrint(Excel.Workbook wb, ref bool cancel)
        {
            AddInDiagnostics.SafeExecute("OnWorkbookBeforePrint", () =>
            {
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

        private void OnSheetSelectionChange(object sh, Excel.Range target)
        {
            AddInDiagnostics.SafeExecute("OnSheetSelectionChange", () =>
            {
                _quickFixController?.OnSheetSelectionChange(sh, target);
            });
        }

        private void OnSheetBeforeRightClick(object sh, Excel.Range target, ref bool cancel)
        {
            bool localCancel = cancel;
            AddInDiagnostics.SafeExecute("OnSheetBeforeRightClick", () =>
            {
                _quickFixController?.OnSheetBeforeRightClick(sh, target, ref localCancel);
            });
            cancel = localCancel;
        }

        private void OnSheetBeforeDoubleClick(object sh, Excel.Range target, ref bool cancel)
        {
            bool localCancel = cancel;
            AddInDiagnostics.SafeExecute("OnSheetBeforeDoubleClick", () =>
            {
                _quickFixController?.OnSheetBeforeDoubleClick(sh, target, ref localCancel);
            });
            cancel = localCancel;
        }

        private void OnSheetActivate(object sh)
        {
            AddInDiagnostics.SafeExecute("OnSheetActivate", () =>
            {
                _quickFixController?.CloseActivePopup();
                _scannerToolkit?.RefreshVisualUnderlinesForActiveWorkbook();

                if (_scannerToolkit != null &&
                    _scannerToolkit.IsLiveAutoScanEnabled &&
                    _scannerToolkit.CurrentScope == SpreadsheetScanScope.ActiveSheet)
                {
                    _scannerToolkit.ScheduleDeferredScanScope(SpreadsheetScanScope.ActiveSheet, 220);
                }
                else
                {
                    _quickFixController?.ScheduleRefreshForActiveCell(90);
                }
            });
        }

        private void OnWindowDeactivate(Excel.Workbook wb, Excel.Window wn)
        {
            AddInDiagnostics.SafeExecute("OnWindowDeactivate", () =>
            {
                _quickFixController?.CloseActivePopup();
                _wavyOverlay?.HideOverlayImmediate();
            });
        }

        private void OnWindowActivate(Excel.Workbook wb, Excel.Window wn)
        {
            AddInDiagnostics.SafeExecute("OnWindowActivate", () =>
            {
                PruneClosedWindowTaskPanes();

                if (!string.IsNullOrEmpty(_uiLanguageConfigPath))
                {
                    UiLanguage savedLang = UiLocalizer.LoadSavedLanguage(_uiLanguageConfigPath);
                    if (savedLang != UiLocalizer.CurrentLanguage)
                    {
                        UiLocalizer.CurrentLanguage = savedLang;
                        foreach (var kv in _windowTaskPanes.Values)
                        {
                            try { kv.Control.ApplyLocalization(); } catch { }
                        }
                        _ribbonUi?.Invalidate();
                    }
                }

                _scannerToolkit?.RefreshEcoActiveState();
                _scannerToolkit?.RefreshVisualUnderlinesForActiveWorkbook();
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                int winHwnd = 0;
                try { winHwnd = wn?.Hwnd ?? _excelApp?.ActiveWindow?.Hwnd ?? 0; } catch { }
                if (winHwnd != 0 && _windowTaskPanes.TryGetValue(winHwnd, out var paneEntry) && !_settledPaneWidthHwnds.Contains(winHwnd))
                {
                    ScheduleStartupPaneWidthEnforcement(winHwnd, paneEntry.Pane, paneEntry.Control, forceIfNarrow: false);
                }

                if (ctrl != null && _scannerToolkit != null)
                {
                    ctrl.SetEcoModeState(EcoPerformanceGovernor.CurrentPreference, _scannerToolkit.IsEcoActive);
                    if (!_scannerToolkit.IsScanning)
                    {
                        ctrl.SetScanOutcomeMetadata(_scannerToolkit.GetLastScanMetadataForActiveWorkbook());
                        ctrl.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                    }
                    ctrl.SetUndoState(_scannerToolkit.UndoManager.UndoCount, _scannerToolkit.UndoManager.LatestDescription);
                }
                _quickFixController?.ScheduleRefreshForActiveCell(90);
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
                _ribbonUi?.InvalidateControl("BtnReviewTogglePane");
            });
        }

        private void OnWorkbookOpen(Excel.Workbook wb)
        {
            AddInDiagnostics.SafeExecute("OnWorkbookOpen", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                EnforceActiveWindowStartupPaneWidth(forceIfNarrow: false);
                _scannerToolkit?.ScheduleDeferredScanScope(SpreadsheetScanScope.ActiveSheet, 280);
            });
        }

        private void OnNewWorkbook(Excel.Workbook wb)
        {
            AddInDiagnostics.SafeExecute("OnNewWorkbook", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                EnforceActiveWindowStartupPaneWidth(forceIfNarrow: false);
            });
        }

        private void OnWorkbookBeforeClose(Excel.Workbook wb, ref bool cancel)
        {
            bool willClose = !cancel;
            AddInDiagnostics.SafeExecute("OnWorkbookBeforeClose", () =>
            {
                _quickFixController?.CloseActivePopup();
                _wavyOverlay?.HideOverlayImmediate();
                string? wbName = null;
                try { wbName = wb?.Name; } catch { }
                if (willClose && !string.IsNullOrEmpty(wbName) && string.Equals(wbName, _activeReviewWorkbookName, StringComparison.OrdinalIgnoreCase))
                {
                    CloseActiveExcelReviewDialog();
                }
                if (willClose && !string.IsNullOrEmpty(wbName))
                {
                    _sessionFixedByWorkbook.Remove(wbName!);
                }
                _scannerToolkit?.PurgeClosedWorkbook(wbName);
                PruneClosedWindowTaskPanes();
                try
                {
                    if (willClose && (_excelApp?.Workbooks?.Count ?? 0) <= 1)
                    {
                        _proofingEngine?.TrimMemoryCaches(
                            aggressive: EcoPerformanceGovernor.Hardware.IsUltraLowMemoryHost ||
                                        EcoPerformanceGovernor.CurrentPreference == EcoModePreference.AlwaysOn);
                    }
                }
                catch { }
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
                {
                    _windowTaskPanes.Remove(h);
                    _settledPaneWidthHwnds.Remove(h);
                    StopAndRemovePaneWidthTimer(h);
                }
            }
            catch { }
        }

        private void StopAndRemovePaneWidthTimer(int hwnd)
        {
            if (_paneWidthTimers.TryGetValue(hwnd, out var timer))
            {
                _paneWidthTimers.Remove(hwnd);
                try
                {
                    timer.Stop();
                    timer.Dispose();
                }
                catch { }
            }
        }

        private bool HasOpenWorkbook()
        {
            try
            {
                return _excelApp != null && _excelApp.Workbooks != null && _excelApp.Workbooks.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private void EnforceActiveWindowStartupPaneWidth(bool forceIfNarrow)
        {
            try
            {
                int hwnd = _excelApp?.ActiveWindow?.Hwnd ?? 0;
                if (hwnd != 0 && _windowTaskPanes.TryGetValue(hwnd, out var entry))
                {
                    ScheduleStartupPaneWidthEnforcement(hwnd, entry.Pane, entry.Control, forceIfNarrow);
                }
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
                    {
                        existing.Pane.Visible = true;
                    }
                    if (autoShow || !_settledPaneWidthHwnds.Contains(hwnd))
                    {
                        ScheduleStartupPaneWidthEnforcement(hwnd, existing.Pane, existing.Control, forceIfNarrow: false);
                    }
                    return existing.Control;
                }

                Office.CustomTaskPane pane = _ctpFactory.CreateCTP(
                    "MyanmarSpellChecker.ExcelTaskPaneControl",
                    UiLocalizer.Pick("Myanmar Spell Checker", "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး"),
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
                ctrl.SetEcoModeState(
                    EcoPerformanceGovernor.CurrentPreference,
                    _scannerToolkit?.IsEcoActive ?? EcoPerformanceGovernor.Hardware.IsLowSpecHardware);

                WireTaskPaneEvents(ctrl);

                pane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;
                pane.VisibleStateChange += ctp =>
                {
                    try
                    {
                        if (ctp.Visible && !_settledPaneWidthHwnds.Contains(hwnd))
                        {
                            ScheduleStartupPaneWidthEnforcement(hwnd, ctp, ctrl, forceIfNarrow: false);
                        }
                        _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
                        _ribbonUi?.InvalidateControl("BtnReviewTogglePane");
                    }
                    catch { }
                };

                _windowTaskPanes[hwnd] = (pane, ctrl);
                if (autoShow)
                {
                    pane.Visible = true;
                    ScheduleStartupPaneWidthEnforcement(hwnd, pane, ctrl, forceIfNarrow: true);
                }
                else
                {
                    EnsureComfortableStartupPaneWidth(hwnd, pane, ctrl, forceIfNarrow: false);
                }

                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
                _ribbonUi?.InvalidateControl("BtnReviewTogglePane");
                return ctrl;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("GetOrCreateActiveWindowTaskPaneControl", ex);
                return null;
            }
        }

        private void ScheduleStartupPaneWidthEnforcement(
            int winHwnd,
            Office.CustomTaskPane pane,
            ExcelProofingTaskPaneControl ctrl,
            bool forceIfNarrow)
        {
            EnsureComfortableStartupPaneWidth(winHwnd, pane, ctrl, forceIfNarrow);
            if (_settledPaneWidthHwnds.Contains(winHwnd) && !forceIfNarrow)
                return;

            StopAndRemovePaneWidthTimer(winHwnd);
            int ticks = 0;
            var timer = new Timer { Interval = 160 };
            timer.Tick += (s, e) =>
            {
                ticks++;
                try
                {
                    EnsureComfortableStartupPaneWidth(winHwnd, pane, ctrl, forceIfNarrow);
                }
                catch { }

                if (_settledPaneWidthHwnds.Contains(winHwnd) || ticks >= 3)
                {
                    StopAndRemovePaneWidthTimer(winHwnd);
                }
            };
            _paneWidthTimers[winHwnd] = timer;
            timer.Start();
        }

        private void EnsureComfortableStartupPaneWidth(
            int winHwnd,
            Office.CustomTaskPane pane,
            ExcelProofingTaskPaneControl ctrl,
            bool forceIfNarrow)
        {
            try
            {
                if (!pane.Visible)
                    return;

                IntPtr hostPtr = new IntPtr(winHwnd);
                int dpi = Math.Max(UiFontCache.GetWindowDpi(hostPtr), UiFontCache.GetEffectiveDpi(ctrl));
                float dpiScale = Math.Max(1.0f, dpi / 96.0f);
                int hostPhysicalWidth = UiFontCache.GetWindowWidthPx(hostPtr);

                bool isCompactLaptop = hostPhysicalWidth > 0 && hostPhysicalWidth <= (int)Math.Round(1720 * dpiScale);
                int desiredWidth = (int)Math.Round((isCompactLaptop ? 365 : 410) * dpiScale);
                int maxSafeWidth = Math.Max((int)Math.Round(290 * dpiScale), (int)(hostPhysicalWidth * (isCompactLaptop ? 0.34 : 0.40)));
                int targetWidth = Math.Min(desiredWidth, maxSafeWidth);
                int minStartupWidth = Math.Min((int)Math.Round(300 * dpiScale), targetWidth);

                if (!_settledPaneWidthHwnds.Contains(winHwnd))
                {
                    int initialPaneWidth = pane.Width;
                    if (pane.Width < minStartupWidth)
                    {
                        try
                        {
                            pane.Width = targetWidth;
                        }
                        catch
                        {
                            try { pane.Width = Math.Max(320, (int)(hostPhysicalWidth * 0.36)); } catch { }
                        }
                    }

                    if (ctrl.IsHandleCreated && ctrl.Width > pane.Width * 1.35 && pane.Width > 520)
                    {
                        try
                        {
                            pane.Width = Math.Max(360, (int)Math.Round(targetWidth / dpiScale));
                        }
                        catch { }
                    }

                    if (!ctrl.IsDisposed && (pane.Width != initialPaneWidth || ctrl.NeedsDpiRefresh()))
                    {
                        ctrl.RefreshDpiMetricsAndLayout();
                    }

                    if (HasOpenWorkbook() &&
                        pane.Visible &&
                        ctrl.IsHandleCreated &&
                        ctrl.Visible &&
                        pane.Width >= minStartupWidth &&
                        ctrl.Width >= (int)(minStartupWidth * 0.72))
                    {
                        _settledPaneWidthHwnds.Add(winHwnd);
                    }
                }
            }
            catch { }
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

        private void SyncEcoStateAcrossTaskPanes(bool ecoActive)
        {
            foreach (var kv in _windowTaskPanes.Values)
            {
                try { kv.Control.SetEcoModeState(EcoPerformanceGovernor.CurrentPreference, ecoActive); } catch { }
            }
        }

        private void ToggleUiLanguageFromUi()
        {
            UiLanguage next = UiLocalizer.ToggleLanguage();
            UiLocalizer.SaveLanguage(_uiLanguageConfigPath, next);

            foreach (var kv in _windowTaskPanes.Values)
            {
                try { kv.Control.ApplyLocalization(); } catch { }
            }

            _ribbonUi?.Invalidate();

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.ShowToastBanner(UiLocalizer.Pick(
                "✓ Interface language switched to English. Click [A/က EN] anytime to switch back to Myanmar.",
                "✓ စနစ်မျက်နှာပြင်ဘာသာစကားကို မြန်မာဘာသာသို့ ပြောင်းလဲပြီးပါပြီ။ [A/က မြန်မာ] ခလုတ်ကိုနှိပ်၍ အင်္ဂလိပ်သို့ ပြန်ပြောင်းနိုင်ပါသည်။"));
        }

        private void CycleEcoModeFromUi()
        {
            EcoModePreference next = EcoPerformanceGovernor.CyclePreference(EcoPerformanceGovernor.CurrentPreference);
            ApplyEcoModePreferenceFromUi(next);
        }

        private void ApplyEcoModePreferenceFromUi(EcoModePreference next)
        {
            EcoPerformanceGovernor.CurrentPreference = next;
            EcoPerformanceGovernor.SavePreference(_ecoModeConfigPath, next);

            _scannerToolkit?.RefreshEcoActiveState();
            bool ecoActive = _scannerToolkit?.IsEcoActive ?? EcoPerformanceGovernor.ComputeBudgets(isLargeFile: false).IsEcoActive;
            if (ecoActive)
            {
                _proofingEngine?.TrimMemoryCaches(
                    aggressive: EcoPerformanceGovernor.Hardware.IsUltraLowMemoryHost ||
                                next == EcoModePreference.AlwaysOn);
            }

            SyncEcoStateAcrossTaskPanes(ecoActive);

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            string toast = next switch
            {
                EcoModePreference.AlwaysOn => UiLocalizer.Pick(
                    "● Eco Mode: ON — minimizes CPU & RAM usage for smooth spreadsheet editing.",
                    "● ပေါ့ပါးစနစ် (Eco Mode: ON) — CPU နှင့် RAM အနည်းဆုံးဖြင့် စက်မလေးစေဘဲ စစ်ဆေးပေးပါမည်။"),
                EcoModePreference.Off => UiLocalizer.Pick(
                    "○ Eco Mode: OFF (Fast Mode) — running at maximum multi-core performance.",
                    "○ ပေါ့ပါးစနစ် ပိတ်ထားသည် (Fast Mode) — စွမ်းဆောင်ရည်အပြည့်ဖြင့် စစ်ဆေးပါမည်။"),
                _ => UiLocalizer.Pick(
                    "◐ Eco Mode: Auto — smartly balances fast scanning with large-workbook protection.",
                    "◐ ပေါ့ပါးစနစ် (Auto) — စက်အားနည်းလျှင် သို့မဟုတ် အကွက်များလျှင် အလိုအလျောက် ချိန်ညှိစစ်ဆေးပေးပါမည်။")
            };
            ctrl?.ShowToastBanner(toast);
        }

        private void WireTaskPaneEvents(ExcelProofingTaskPaneControl ctrl)
        {
            ctrl.RequestScanScope += scope => TriggerScanWithDbHotReload(scope);
            ctrl.RequestCancelScan += () => _scannerToolkit?.CancelUserScan();
            ctrl.RequestToggleLanguage += ToggleUiLanguageFromUi;
            ctrl.RequestCycleEcoMode += CycleEcoModeFromUi;
            ctrl.RequestSetEcoMode += ApplyEcoModePreferenceFromUi;
            ctrl.RequestFixAllIssues += ExecuteFixAllInActiveScope;
            ctrl.RequestUndoLastAction += ExecuteUndoLastAction;
            ctrl.RequestApplyDockedBatch += approved => ApplyExcelApprovedBatch(ctrl, approved, GetExcelFilterScopeLabel(_activeReviewFilterMode));
            ctrl.RequestPopOutDockedChecklist += entries =>
                OpenOrUpdateExcelModelessReviewStudio(_activeReviewFilterMode, ctrl, entries, forceFloating: true);
            ctrl.RequestClearUnderlines += () => ExecuteClearExcelUnderlines();
            ctrl.RequestLicenseDialog += ShowLicenseDialog;
            ctrl.RequestChangeStyleMode += ApplyStyleModeAndRescan;
            ctrl.RequestSelectIssueInSheet += item => _scannerToolkit?.SelectAndScrollToIssue(item);

            ctrl.RequestApplySuggestion += ApplySingleIssueFixFromUi;
            ctrl.RequestApplyBatchSuggestion += ApplySameWordBatchFixFromUi;
            ctrl.RequestIgnoreOnce += IgnoreIssueFromUi;
            ctrl.RequestAddToDictionary += AddIssueWordToDictionaryFromUi;

            ctrl.RequestDictionarySearch += query =>
                _proofingEngine?.SearchDictionary(query, 45) ?? new List<DictionaryEntry>();
            ctrl.RequestIsEngineReady += () =>
                _proofingEngine != null;
            ctrl.RequestGetEngineMutationVersion += () =>
                _proofingEngine?.MutationVersion ?? 0L;
            ctrl.RequestPrewarmDictionary += () =>
                _proofingEngine?.PrewarmDictionaryIndexes();
            ctrl.RequestIsDictionaryIndexReady += () =>
                _proofingEngine?.IsDictionarySearchReady ?? true;
            ctrl.RequestSearchReferenceCorpus += (query, incProverbs, incEncyc) =>
                _proofingEngine?.SearchReferenceCorpus(query, incProverbs, incEncyc, 40) ?? new List<DictionaryEntry>();
            ctrl.RequestGetFeaturedKnowledge += () =>
                _proofingEngine?.GetFeaturedKnowledgeEntries(8) ?? new List<DictionaryEntry>();
            ctrl.RequestGetFeaturedProverbs += () =>
                _proofingEngine?.GetFeaturedProverbEntries(10) ?? new List<DictionaryEntry>();
            ctrl.RequestGetSynonyms += word =>
                _proofingEngine?.GetSynonyms(word, 8) ?? new List<string>();
            ctrl.RequestGetAntonyms += word =>
                _proofingEngine?.GetAntonyms(word, 8) ?? new List<string>();
            ctrl.RequestGetCollocations += word =>
                _proofingEngine?.GetTopCollocations(word, 6) ?? new List<string>();
            ctrl.RequestGetBookmarkedEntries += words =>
                _proofingEngine?.LookupBookmarkedEntries(words) ?? new List<DictionaryEntry>();
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
                    _scannerToolkit.ScanScopeAsync(_scannerToolkit.CurrentScope);
                return true;
            };

            // Toolkit Actions
            ctrl.RequestConvertLegacyFonts += selectionOnly =>
            {
                if (_scannerToolkit == null) return;
                ExcelToolkitOutcome res;
                try { res = _scannerToolkit.ConvertLegacyFontsInScope(selectionOnly); }
                finally { ctrl.SetScanningState(false); }
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestRepairBrokenPdfText += selectionOnly =>
            {
                if (_scannerToolkit == null) return;
                ExcelToolkitOutcome res;
                try { res = _scannerToolkit.RepairBrokenPdfInScope(selectionOnly); }
                finally { ctrl.SetScanningState(false); }
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestPasteCleanClipboard += () =>
            {
                if (_scannerToolkit == null) return;
                ExcelToolkitOutcome res = _scannerToolkit.PasteAndRepairClipboardIntoActiveCell();
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestFormatNumerals += (toMyanmar, selectionOnly) =>
            {
                if (_scannerToolkit == null) return;
                ExcelToolkitOutcome res;
                try { res = _scannerToolkit.FormatNumeralsInScope(toMyanmar, selectionOnly); }
                finally { ctrl.SetScanningState(false); }
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestRewriteToOfficialStyle += selectionOnly =>
            {
                if (_scannerToolkit == null) return;
                ExcelToolkitOutcome res;
                try { res = _scannerToolkit.RewriteOfficialProseInScope(selectionOnly); }
                finally { ctrl.SetScanningState(false); }
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestFormatPunctuation += selectionOnly =>
            {
                if (_scannerToolkit == null) return;
                ExcelToolkitOutcome res;
                try { res = _scannerToolkit.FormatPunctuationInScope(selectionOnly); }
                finally { ctrl.SetScanningState(false); }
                ctrl.UpdateQualityReport(null);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestStripAllZwsp += selectionOnly =>
            {
                if (_scannerToolkit == null) return;
                ExcelToolkitOutcome res;
                try { res = _scannerToolkit.StripAllZwspInScope(selectionOnly); }
                finally { ctrl.SetScanningState(false); }
                ctrl.UpdateQualityReport(null);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestRefreshQualityReport += () =>
                _scannerToolkit?.BuildActiveWorkbookQualityReport(forceRefresh: true) ?? new DocumentQualityReport();
            ctrl.RequestExportAuditWorkbook += () =>
                ExecuteExportExcelProofingReport(ProofingReportExportFormat.PrintableA4WordDocument);
            ctrl.RequestExportProofingReport += ExecuteExportExcelProofingReport;
        }

        private string GetActiveExcelWorkbookTitle()
        {
            try
            {
                string? name = _excelApp?.ActiveWorkbook?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    return name!;
            }
            catch { }
            return UiLocalizer.Pick("Untitled Excel Workbook", "အမည်မပေးရသေးသော Excel Workbook");
        }

        private void RecordExcelSessionFix(SpreadsheetCellIssue item, string appliedReplacement)
        {
            if (item == null || string.IsNullOrEmpty(appliedReplacement))
                return;

            string wbKey = !string.IsNullOrWhiteSpace(item.WorkbookName)
                ? item.WorkbookName
                : GetActiveExcelWorkbookTitle();
            if (!_sessionFixedByWorkbook.TryGetValue(wbKey, out var list))
            {
                list = new List<ProofingSessionFixedRecord>();
                _sessionFixedByWorkbook[wbKey] = list;
            }

            if (list.Count >= 2000)
                list.RemoveAt(0);

            list.Add(new ProofingSessionFixedRecord(
                item.DisplayLocation,
                item.Issue.OriginalText ?? string.Empty,
                appliedReplacement,
                DateTime.Now));
        }

        private ProofingAuditReportModel BuildActiveExcelProofingAuditReportModel()
        {
            string wbTitle = GetActiveExcelWorkbookTitle();
            var orderedSheets = _scannerToolkit?.GetVisibleWorksheetNamesForActiveWorkbook() ?? new List<string>();
            var allWorkbookIssues = _scannerToolkit != null
                ? _scannerToolkit.EnsureEntireWorkbookScannedForAuditReport()
                : Array.Empty<SpreadsheetCellIssue>();
            var unresolved = allWorkbookIssues.Where(i => !i.IsResolved).ToList();
            var qReport = _scannerToolkit?.BuildActiveWorkbookQualityReport(forceRefresh: true) ?? new DocumentQualityReport();

            var auditInputs = unresolved
                .Select(item => new ProofingAuditItemInput(
                    item.DisplayLocation,
                    item.CellSnapshotText,
                    item.Issue,
                    (item.Issue.Suggestions != null && item.Issue.Suggestions.Count > 0)
                        ? item.Issue.Suggestions[0].Replacement
                        : string.Empty,
                    pageOrSheetLabel: item.SheetName,
                    paragraphOrCellLabel: $"R{item.Row}C{item.Column}"))
                .ToList();

            IReadOnlyList<ProofingSessionFixedRecord> sessionFixed =
                _sessionFixedByWorkbook.TryGetValue(wbTitle, out var list)
                    ? list.ToList()
                    : Array.Empty<ProofingSessionFixedRecord>();

            return ProofingAuditReportBuilder.Build(
                wbTitle,
                "Microsoft Excel",
                _proofingEngine?.StyleMode ?? _pendingStyleMode,
                qReport,
                auditInputs,
                sessionFixed,
                allOrderedSectionNames: orderedSheets.Count > 0 ? orderedSheets : null);
        }

        private void ExecuteExportExcelProofingReport(ProofingReportExportFormat format)
        {
            if (_scannerToolkit == null)
                return;

            ProofingAuditReportModel model = BuildActiveExcelProofingAuditReportModel();
            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.SetScanScope(_scannerToolkit.CurrentScope);

            switch (format)
            {
                case ProofingReportExportFormat.PrintableA4WordDocument:
                {
                    try
                    {
                        string docxPath = ProofingAuditWordDocumentExporter.SavePrintableA4DocxToDisk(model);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(docxPath)
                        {
                            UseShellExecute = true
                        });
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            $"✓ Opened Printable A4 Word Audit Report (.docx — all {Math.Max(1, model.SectionBreakdowns.Count)} sheet(s) audited: {model.ConfirmedWrongCount} wrong • {model.SuspiciousReviewCount} review).",
                            $"✓ Sheet အားလုံး ({Math.Max(1, model.SectionBreakdowns.Count)} ခု) ပါဝင်သော ပုံနှိပ်ရန်အသင့် A4 Word အစီရင်ခံစာ (.docx) ကို ဖွင့်ပေးပြီးပါပြီ။"));
                    }
                    catch
                    {
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            "⚠ Could not save or open the Printable A4 Word (.docx) Audit Report.",
                            "⚠ A4 Word (.docx) အစီရင်ခံစာ ဖွင့်ရာတွင် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။"), isSuccess: false);
                    }
                    break;
                }
                case ProofingReportExportFormat.FriendlyExcelWorkbook:
                {
                    try
                    {
                        string xlsxPath = ProofingAuditSpreadsheetExporter.SaveFriendlyXlsxToDisk(model);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(xlsxPath)
                        {
                            UseShellExecute = true
                        });
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            $"✓ Opened Multi-Sheet Excel Audit Report (.xlsx — {model.ConfirmedWrongCount} wrong • {model.SuspiciousReviewCount} review needed).",
                            $"✓ Multi-Sheet Excel အစီရင်ခံစာ (.xlsx) ကို ဖွင့်ပေးပြီးပါပြီ (သေချာမှား {model.ConfirmedWrongCount} • စိစစ်ရန် {model.SuspiciousReviewCount})။"));
                    }
                    catch
                    {
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            "⚠ Could not open the Excel (.xlsx) Audit Report.",
                            "⚠ Excel (.xlsx) အစီရင်ခံစာ ဖွင့်ရာတွင် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။"), isSuccess: false);
                    }
                    break;
                }
                case ProofingReportExportFormat.FriendlyCsvSpreadsheet:
                {
                    try
                    {
                        string csvPath = ProofingAuditReportBuilder.SaveFriendlyCsvToDisk(model);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(csvPath)
                        {
                            UseShellExecute = true
                        });
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            $"✓ Saved & opened UTF-8 CSV Audit Report ({Path.GetFileName(csvPath)}).",
                            $"✓ UTF-8 CSV ဇယားဖိုင် အစီရင်ခံစာကို သိမ်းဆည်းဖွင့်ပေးပြီးပါပြီ ({Path.GetFileName(csvPath)})။"));
                    }
                    catch
                    {
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            "⚠ Could not save or open the CSV Audit Report.",
                            "⚠ CSV အစီရင်ခံစာ သိမ်းဆည်းရာတွင် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။"), isSuccess: false);
                    }
                    break;
                }
                case ProofingReportExportFormat.ExecutivePowerPointDeck:
                {
                    try
                    {
                        string pptxPath = ProofingAuditPresentationExporter.SaveExecutivePptxToDisk(model);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pptxPath)
                        {
                            UseShellExecute = true
                        });
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            $"✓ Opened PowerPoint Executive Audit Deck (.pptx — 4 widescreen slides).",
                            $"✓ PowerPoint အမှုဆောင် အစီရင်ခံစာ ဆလိုက်တွဲ (.pptx — ဆလိုက် ၄ ချပ်) ကို ဖွင့်ပေးပြီးပါပြီ။"));
                    }
                    catch
                    {
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            "⚠ Could not save or open the PowerPoint (.pptx) Executive Summary.",
                            "⚠ PowerPoint (.pptx) ဆလိုက်တွဲ ဖွင့်ရာတွင် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။"), isSuccess: false);
                    }
                    break;
                }
                case ProofingReportExportFormat.NativeOfficeDocument:
                {
                    bool ok = _scannerToolkit.GenerateRichAuditReportWorkbook(model);
                    ctrl?.ShowToastBanner(
                        ok
                            ? UiLocalizer.Pick(
                                $"✓ Exported 4-sheet Excel Proofing Audit Workbook ({model.ConfirmedWrongCount} wrong • {model.SuspiciousReviewCount} suspicious • {model.SessionFixedCount} fixed).",
                                $"✓ Sheet ၄ ခုပါ Excel စစ်ဆေးချက် အစီရင်ခံစာ ထုတ်ပေးပြီးပါပြီ (သေချာမှား {model.ConfirmedWrongCount} • ပြန်စစ်ရန် {model.SuspiciousReviewCount} • ပြင်ပြီး {model.SessionFixedCount})။")
                            : UiLocalizer.Pick(
                                "⚠ Could not generate the Excel audit report workbook.",
                                "⚠ Excel အစီရင်ခံစာ ထုတ်ယူစဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။"),
                        ok);
                    break;
                }
                case ProofingReportExportFormat.InteractiveHtmlAndPdf:
                {
                    try
                    {
                        string htmlPath = ProofingAuditReportBuilder.SaveStandaloneInteractiveHtmlToDisk(model);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(htmlPath)
                        {
                            UseShellExecute = true
                        });
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            "✓ Opened interactive HTML Audit Report in browser (click ⎙ Print / Save as PDF inside).",
                            "✓ အပြန်အလှန် စစ်ဆေးနိုင်သော HTML အစီရင်ခံစာကို Browser ဖြင့် ဖွင့်ပေးပြီးပါပြီ (⎙ Print / Save as PDF နှိပ်၍ သိမ်းနိုင်သည်)။"));
                    }
                    catch
                    {
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            "⚠ Could not open HTML Audit Report.",
                            "⚠ HTML အစီရင်ခံစာ ဖွင့်ရာတွင် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။"), isSuccess: false);
                    }
                    break;
                }
                case ProofingReportExportFormat.CopySummaryToClipboard:
                {
                    try
                    {
                        string text = ProofingAuditReportBuilder.BuildClipboardSummaryText(model);
                        Clipboard.SetText(text, TextDataFormat.UnicodeText);
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            "✓ Copied Proofing Audit Summary to Clipboard (ready to paste into Email, Teams, or Telegram).",
                            "✓ စစ်ဆေးချက် အကျဉ်းချုပ်ကို Clipboard သို့ ကူးယူပြီးပါပြီ (Email / Telegram တွင် Paste လုပ်နိုင်သည်)။"));
                    }
                    catch
                    {
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            "⚠ Could not copy summary to Clipboard.",
                            "⚠ Clipboard သို့ ကူးယူရာတွင် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။"), isSuccess: false);
                    }
                    break;
                }
            }
        }

        private void CloseActiveExcelReviewDialog()
        {
            var dlg = _activeReviewDialog;
            _activeReviewDialog = null;
            _activeReviewWorkbookName = string.Empty;
            if (dlg != null && !dlg.IsDisposed)
            {
                try { dlg.Close(); } catch { }
                try { dlg.Dispose(); } catch { }
            }
        }

        private static Func<SpreadsheetCellIssue, bool> GetExcelFilterPredicate(IssueFilterMode filterMode) =>
            filterMode switch
            {
                IssueFilterMode.SpellingOnly => i => !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning,
                IssueFilterMode.GrammarOnly => i => i.Issue.IsGrammarOrStyle,
                IssueFilterMode.ContextOnly => i => i.Issue.IsContextualWarning,
                _ => _ => true
            };

        private static string GetExcelFilterScopeLabel(IssueFilterMode filterMode) =>
            filterMode switch
            {
                IssueFilterMode.SpellingOnly => UiLocalizer.Pick("Spelling Issues", "သတ်ပုံအမှား"),
                IssueFilterMode.GrammarOnly => UiLocalizer.Pick("Grammar / Style Issues", "သဒ္ဒါ/စာဟန်အမှား"),
                IssueFilterMode.ContextOnly => UiLocalizer.Pick("Context / Meaning Checks", "အနက်သံသယ စိစစ်ချက်"),
                _ => UiLocalizer.Pick("All Issues", "အမှားစုစုပေါင်း")
            };

        private List<FixPreviewEntry<SpreadsheetCellIssue>> BuildExcelFixPreviewEntries(IssueFilterMode filterMode)
        {
            if (_scannerToolkit == null)
                return new List<FixPreviewEntry<SpreadsheetCellIssue>>();

            var predicate = GetExcelFilterPredicate(filterMode);
            return _scannerToolkit.GetCachedIssuesForActiveWorkbook()
                .Where(i => !i.IsResolved &&
                            i.Issue.Suggestions != null &&
                            i.Issue.Suggestions.Count > 0 &&
                            predicate(i))
                .Select(item => new FixPreviewEntry<SpreadsheetCellIssue>(
                    item,
                    item.DisplayLocation,
                    item.CellSnapshotText,
                    item.Issue))
                .ToList();
        }

        private int ApplyExcelApprovedBatch(
            ExcelProofingTaskPaneControl? ctrl,
            IReadOnlyDictionary<SpreadsheetCellIssue, string> approvedReplacements,
            string scopeLabel)
        {
            if (_scannerToolkit == null || approvedReplacements == null || approvedReplacements.Count == 0)
                return 0;

            int fixedCount;
            string busyWarn;
            try
            {
                fixedCount = _scannerToolkit.ApplyApprovedFixes(
                    approvedReplacements,
                    UiLocalizer.Pick($"Fix ({scopeLabel})", $"{scopeLabel} စိစစ်ပြင်ဆင်မှု"),
                    out busyWarn);
            }
            finally
            {
                ctrl?.SetScanningState(false);
            }

            ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());

            if (!string.IsNullOrEmpty(busyWarn))
            {
                ctrl?.ShowToastBanner(busyWarn, isSuccess: false);
                return 0;
            }

            if (fixedCount > 0)
            {
                foreach (var kvp in approvedReplacements)
                {
                    if (kvp.Key.IsResolved)
                        RecordExcelSessionFix(kvp.Key, kvp.Value);
                }
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Applied {fixedCount} approved fix(es) safely without touching formulas (↶ Undo available).",
                    $"✓ စိစစ်အတည်ပြုထားသော ({fixedCount}) ခုကို Formula မပျက်စေဘဲ ပြင်ဆင်ပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။"));
            }
            return fixedCount;
        }

        private void ExecuteFixAllInActiveScope(IssueFilterMode filterMode = IssueFilterMode.All)
        {
            if (_scannerToolkit == null)
                return;

            _activeReviewFilterMode = filterMode;
            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            var previewEntries = BuildExcelFixPreviewEntries(filterMode);

            if (previewEntries.Count == 0)
            {
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    "✓ No remaining issues to fix in this scope.",
                    "✓ ပြင်ဆင်ရန် အမှားအယွင်း မကျန်ရှိတော့ပါ။"));
                return;
            }

            var prefs = ReviewWindowPreferencesStore.Load("excel");
            if (prefs.PreferDockedInTaskPane && ctrl != null)
            {
                CloseActiveExcelReviewDialog();
                ctrl.EnterDockedChecklistMode(previewEntries);
                return;
            }

            OpenOrUpdateExcelModelessReviewStudio(filterMode, ctrl, previewEntries, forceFloating: true);
        }

        private void OpenOrUpdateExcelModelessReviewStudio(
            IssueFilterMode filterMode,
            ExcelProofingTaskPaneControl? ctrl,
            IReadOnlyList<FixPreviewEntry<SpreadsheetCellIssue>> previewEntries,
            bool forceFloating)
        {
            if (_scannerToolkit == null)
                return;

            string wbName = "Excel";
            try { if (_excelApp?.ActiveWorkbook != null && !string.IsNullOrWhiteSpace(_excelApp.ActiveWorkbook.Name)) wbName = _excelApp.ActiveWorkbook.Name; } catch { }
            string scopeLabel = GetExcelFilterScopeLabel(filterMode);
            string scopeTitle = $"{wbName} — {scopeLabel}";

            if (_activeReviewDialog != null && !_activeReviewDialog.IsDisposed)
            {
                _activeReviewWorkbookName = wbName;
                _activeReviewFilterMode = filterMode;
                if (_activeReviewDialog.WindowState == FormWindowState.Minimized)
                    _activeReviewDialog.WindowState = FormWindowState.Normal;
                _activeReviewDialog.ReloadEntriesFromHost(previewEntries, scopeTitle);
                _activeReviewDialog.Show();
                _activeReviewDialog.BringToFront();
                _activeReviewDialog.Activate();
                return;
            }

            var callbacks = new FixPreviewStudioCallbacks<SpreadsheetCellIssue>
            {
                HostTag = "excel",
                OnNavigateToItem = target => _scannerToolkit.SelectAndScrollToIssue(target),
                OnApplyBatch = approved => ApplyExcelApprovedBatch(ctrl, approved, scopeLabel),
                OnIgnoreItems = items =>
                {
                    foreach (var item in items)
                    {
                        _proofingEngine?.IgnoreWordInSession(item.Issue.OriginalText);
                        item.IsResolved = true;
                    }
                    _scannerToolkit.RefreshVisualUnderlinesForActiveWorkbook();
                    ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                },
                OnAddWordToDictionary = (word, items) =>
                {
                    _proofingEngine?.AddCustomWord(word, _customDictFilePath);
                    string normAdded = UnicodeNormalizer.NormalizeCanonicalOrder(word.Trim());
                    foreach (var cached in _scannerToolkit.GetCachedIssuesForActiveWorkbook())
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                            cached.IsResolved = true;
                    }
                    _scannerToolkit.RefreshVisualUnderlinesForActiveWorkbook();
                    ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                    ctrl?.ShowToastBanner(UiLocalizer.Pick(
                        $"✓ Added \"{word}\" to your Custom Dictionary.",
                        $"✓ \"{word}\" ကို မှတ်သားစာလုံးစာရင်းသို့ ထည့်သွင်းပြီးပါပြီ။"));
                },
                OnRefreshEntries = () => BuildExcelFixPreviewEntries(_activeReviewFilterMode),
                OnUndoLastFix = ExecuteUndoLastAction,
                OnDockInTaskPane = snapshot =>
                {
                    var paneCtrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                    paneCtrl?.EnterDockedChecklistMode(snapshot);
                },
                OnExportReport = ExecuteExportExcelProofingReport
            };

            if (forceFloating)
            {
                var p = ReviewWindowPreferencesStore.Load("excel");
                if (p.PreferDockedInTaskPane)
                {
                    p.PreferDockedInTaskPane = false;
                    ReviewWindowPreferencesStore.Save("excel", p);
                }
            }

            var dlg = new FixAllPreviewDialog<SpreadsheetCellIssue>(
                previewEntries,
                scopeTitle,
                callbacks.OnNavigateToItem,
                callbacks);

            _activeReviewDialog = dlg;
            _activeReviewWorkbookName = wbName;
            dlg.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_activeReviewDialog, dlg))
                {
                    _activeReviewDialog = null;
                    _activeReviewWorkbookName = string.Empty;
                }
                try { dlg.Dispose(); } catch { }
            };

            // Modeless Show(): Excel workbook & Task Pane stay 100% live, scrollable, editable, and minimizable!
            dlg.Show();
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
                        _proofingEngine.HotSwapTrie(freshTrie);
                        _proofingEngine.LoadCustomDictionaryFile(ministryDict);
                        _proofingEngine.LoadCustomDictionaryFile(_customDictFilePath);
                        GC.Collect(1, GCCollectionMode.Optimized, blocking: false);

                        SafeBeginInvokeUi(_uiMarshaller, () =>
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
                            _scannerToolkit.ScanScopeAsync(scope);
                        });
                    }
                    catch
                    {
                        SafeBeginInvokeUi(_uiMarshaller, () => _scannerToolkit?.ScanScopeAsync(scope));
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _dbHotReloadInProgress, 0);
                    }
                });
                return;
            }

            _scannerToolkit.ScanScopeAsync(scope);
        }

        private static void SafeBeginInvokeUi(Control? marshaller, Action action)
        {
            if (marshaller == null || marshaller.IsDisposed || !marshaller.IsHandleCreated)
                return;

            try
            {
                marshaller.BeginInvoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
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

        private void ApplySingleIssueFixFromUi(SpreadsheetCellIssue item, string replacement)
        {
            if (_scannerToolkit == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
            if (_scannerToolkit.ApplySingleFix(item, replacement, out string err))
            {
                RecordExcelSessionFix(item, replacement);
                ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Applied \"{replacement}\" in {item.DisplayLocation} (↶ Undo available).",
                    $"✓ {item.DisplayLocation} အကွက်တွင် \"{replacement}\" ဖြင့် ပြင်ဆင်ပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။"));
            }
            else if (!string.IsNullOrEmpty(err))
            {
                ctrl?.ShowToastBanner(err, isSuccess: false);
            }
        }

        private void ApplySameWordBatchFixFromUi(string orig, string rep)
        {
            if (_scannerToolkit == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
            string targetNorm = UnicodeNormalizer.NormalizeCanonicalOrder((orig ?? string.Empty).Trim());
            var preMatching = _scannerToolkit.GetCachedIssuesForActiveWorkbook()
                .Where(i => !i.IsResolved &&
                            string.Equals(UnicodeNormalizer.NormalizeCanonicalOrder((i.Issue.OriginalText ?? string.Empty).Trim()), targetNorm, StringComparison.Ordinal))
                .ToList();
            int count = _scannerToolkit.ApplySameWordBatchFix(orig ?? string.Empty, rep ?? string.Empty, out string warn);
            if (count > 0)
            {
                foreach (var matched in preMatching)
                {
                    if (matched.IsResolved)
                        RecordExcelSessionFix(matched, rep ?? string.Empty);
                }
            }
            ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
            if (count > 0)
            {
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Updated {count} matching cell(s) with \"{rep}\" (↶ Undo available).",
                    $"✓ တူညီသောအမှား ({count}) နေရာလုံးကို \"{rep}\" ဖြင့် တစ်ပြိုင်နက် ပြင်ဆင်ပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။"));
            }
            else if (!string.IsNullOrEmpty(warn))
            {
                ctrl?.ShowToastBanner(warn, isSuccess: false);
            }
        }

        private void IgnoreIssueFromUi(SpreadsheetCellIssue item)
        {
            _proofingEngine?.IgnoreWordInSession(item.Issue.OriginalText);
            item.IsResolved = true;
            if (_scannerToolkit != null)
            {
                _scannerToolkit.RefreshVisualUnderlinesForActiveWorkbook();
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
            }
        }

        private void AddIssueWordToDictionaryFromUi(SpreadsheetCellIssue item)
        {
            if (_proofingEngine == null || item == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
            string confirmMsg = item.Issue.BuildAddCustomWordConfirmationMessage(out string confirmTitle, out bool isKnownErr);
            DialogResult confirmRes = ctrl != null && ctrl.IsHandleCreated && !ctrl.IsDisposed
                ? MessageBox.Show(
                    ctrl,
                    confirmMsg,
                    confirmTitle,
                    MessageBoxButtons.YesNo,
                    isKnownErr ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2)
                : MessageBox.Show(
                    confirmMsg,
                    confirmTitle,
                    MessageBoxButtons.YesNo,
                    isKnownErr ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
            if (confirmRes != DialogResult.Yes)
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
                ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActiveWorkbook());
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Added \"{item.Issue.OriginalText}\" to your Personal Dictionary (Word + Excel).",
                    $"✓ \"{item.Issue.OriginalText}\" ကို ကိုယ်ပိုင်အဘိဓာန် (Word + Excel) သို့ မှတ်သားပြီးပါပြီ။"));
            }
        }

        private void ExecuteClearExcelUnderlines()
        {
            if (_scannerToolkit == null)
                return;

            _quickFixController?.CloseActivePopup();
            _wavyOverlay?.HideOverlayImmediate();

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            int cleaned = _scannerToolkit.ClearAllVisualUnderlinesInActiveWorkbook();
            ctrl?.ShowToastBanner(
                cleaned > 0
                    ? UiLocalizer.Pick(
                        $"✓ Hid screen-only wavy underlines for {cleaned} cell(s) (cell text & .xlsx file untouched).",
                        $"✓ အကွက် ({cleaned}) ကွက်၏ ဖန်သားပြင်ပေါ်ရှိ မျဉ်းလှိုင်းများကို ဖျောက်ပြီးပါပြီ (Cell အရောင်နှင့် .xlsx ဖိုင် လုံးဝမပြောင်းလဲပါ)။")
                    : UiLocalizer.Pick(
                        "✓ No active proofing underlines on screen.",
                        "✓ ဖန်သားပြင်ပေါ်တွင် ဖျောက်ရန် စစ်ဆေးရေးမျဉ်း မရှိပါ။"));
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
            _ribbonUi?.InvalidateControl("MnuStyleMode");
            _ribbonUi?.InvalidateControl("ChkStyleCasual");
            _ribbonUi?.InvalidateControl("ChkStyleStandard");
            _ribbonUi?.InvalidateControl("ChkStyleOfficial");

            if (_proofingEngine == null || _scannerToolkit == null)
                return;

            _proofingEngine.StyleMode = mode;
            _scannerToolkit.ScanScopeAsync(_scannerToolkit.CurrentScope);
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
      <tab idMso='TabReview'>
        <group id='GrpReviewMyanmarSpellChecker' getLabel='GetRibbonLabel'>
          <toggleButton id='BtnReviewTogglePane'
                        getLabel='GetRibbonLabel'
                        size='large'
                        getImage='GetRibbonBrandImage'
                        getPressed='GetTaskPanePressed'
                        onAction='OnRibbonToggleTaskPane'
                        getScreentip='GetRibbonScreentip' />
          <button id='BtnReviewScanNow'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='AcceptInvitation'
                  onAction='OnRibbonScanSheet'
                  getScreentip='GetRibbonScreentip' />
        </group>
      </tab>
      <tab id='TabMyanmarExcelProofing' getLabel='GetRibbonLabel'>
        <group id='GrpExcelScanAndFix' getLabel='GetRibbonLabel'>
          <button id='BtnScanActiveSheet'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='SpellingAndGrammar'
                  onAction='OnRibbonScanSheet'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnScanSelection'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='SelectCell'
                  onAction='OnRibbonScanSelection'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnScanWorkbook'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='WorkbookViewsCustom'
                  onAction='OnRibbonScanWorkbook'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnFixAllIssues'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='AcceptInvitation'
                  onAction='OnRibbonFixAllIssues'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnUndoLastFix'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='Undo'
                  getEnabled='GetUndoEnabled'
                  onAction='OnRibbonUndoLastFix'
                  getScreentip='GetRibbonScreentip' />
        </group>
        <group id='GrpExcelStyleAndLive' getLabel='GetRibbonLabel'>
          <menu id='MnuStyleMode'
                getLabel='GetRibbonLabel'
                size='large'
                imageMso='FileDocumentInspect'
                getScreentip='GetRibbonScreentip'>
            <checkBox id='ChkStyleCasual'
                      getLabel='GetRibbonLabel'
                      getPressed='GetStyleModeItemPressed'
                      onAction='OnRibbonSelectStyleMode'
                      getScreentip='GetRibbonScreentip' />
            <checkBox id='ChkStyleStandard'
                      getLabel='GetRibbonLabel'
                      getPressed='GetStyleModeItemPressed'
                      onAction='OnRibbonSelectStyleMode'
                      getScreentip='GetRibbonScreentip' />
            <checkBox id='ChkStyleOfficial'
                      getLabel='GetRibbonLabel'
                      getPressed='GetStyleModeItemPressed'
                      onAction='OnRibbonSelectStyleMode'
                      getScreentip='GetRibbonScreentip' />
          </menu>
          <toggleButton id='BtnToggleLiveScan'
                        getLabel='GetRibbonLabel'
                        size='large'
                        imageMso='ReviewTrackChanges'
                        getPressed='GetLiveScanPressed'
                        onAction='OnRibbonToggleLiveScan'
                        getScreentip='GetRibbonScreentip' />
          <toggleButton id='BtnToggleVisualUnderlines'
                        getLabel='GetRibbonLabel'
                        size='normal'
                        imageMso='FontColorPicker'
                        getPressed='GetVisualUnderlinesPressed'
                        onAction='OnRibbonToggleVisualUnderlines'
                        getScreentip='GetRibbonScreentip' />
          <button id='BtnClearExcelUnderlines'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='ClearFormatting'
                  onAction='OnRibbonClearUnderlines'
                  getScreentip='GetRibbonScreentip' />
        </group>
        <group id='GrpExcelDictionary' getLabel='GetRibbonLabel'>
          <button id='BtnSearchDictionary'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='LookUpReference'
                  onAction='OnRibbonSearchDictionary'
                  getScreentip='GetRibbonScreentip' />
          <toggleButton id='BtnToggleTaskPane'
                        getLabel='GetRibbonLabel'
                        size='large'
                        getImage='GetRibbonBrandImage'
                        getPressed='GetTaskPanePressed'
                        onAction='OnRibbonToggleTaskPane'
                        getScreentip='GetRibbonScreentip' />
          <button id='BtnManageCustomWords'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='BookmarkInsert'
                  onAction='OnRibbonManageCustomWords'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnToggleUiLanguage'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='Translate'
                  onAction='OnRibbonToggleLanguage'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnLicenseActivation'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='Info'
                  onAction='OnRibbonOpenLicense'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnCheckUpdates'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='Refresh'
                  onAction='OnRibbonCheckUpdates'
                  getScreentip='GetRibbonScreentip' />
          <menu id='MnuUserGuide'
                getLabel='GetRibbonLabel'
                size='normal'
                imageMso='Help'
                getScreentip='GetRibbonScreentip'>
            <button id='BtnGuideHtml'
                    getLabel='GetRibbonLabel'
                    imageMso='WebPagePreview'
                    onAction='OnRibbonOpenHtmlGuide'
                    getScreentip='GetRibbonScreentip' />
            <button id='BtnGuideDocx'
                    getLabel='GetRibbonLabel'
                    imageMso='FilePrintPreview'
                    onAction='OnRibbonOpenWordGuide'
                    getScreentip='GetRibbonScreentip' />
          </menu>
        </group>
        <group id='GrpExcelTypography' getLabel='GetRibbonLabel'>
          <button id='BtnConvertFonts'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='FontDialog'
                  onAction='OnRibbonConvertFonts'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnRepairBrokenPdf'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='FileSaveAsPdfOrXps'
                  onAction='OnRibbonRepairBrokenPdf'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnPasteCleanClipboard'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='Paste'
                  onAction='OnRibbonPasteCleanClipboard'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnNumeralsToMyanmar'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='BulletsAndNumberingDialog'
                  onAction='OnRibbonNumeralsToMyanmar'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnCleanPunctuation'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='ParagraphMarks'
                  onAction='OnRibbonCleanPunctuation'
                  getScreentip='GetRibbonScreentip' />
        </group>
        <group id='GrpExcelAccounting' getLabel='GetRibbonLabel'>
          <button id='BtnOpenToolkitPane'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='DateAndTimeInsert'
                  onAction='OnRibbonOpenToolkitPane'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnRewriteOfficialProse'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='Translate'
                  onAction='OnRibbonRewriteOfficialProse'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnExportAuditReport'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='FilePrintPreview'
                  onAction='OnRibbonExportAuditReport'
                  getScreentip='GetRibbonScreentip' />
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>";
        }

        public void OnRibbonLoad(Office.IRibbonUI ribbonUi)
        {
            _ribbonUi = ribbonUi;
            if (MyanmarSpellChecker.Core.Updates.PluginUpdateService.ShouldRunOptInWeeklyCheckNow())
            {
                Task.Run(async () =>
                {
                    var res = await MyanmarSpellChecker.Core.Updates.PluginUpdateService.CheckGitHubReleaseAsync().ConfigureAwait(false);
                    if (res.IsUpdateAvailable)
                    {
                        try { _ribbonUi?.InvalidateControl("BtnCheckUpdates"); } catch { }
                    }
                });
            }
        }

        public System.Drawing.Bitmap GetRibbonBrandImage(Office.IRibbonControl control) =>
            UiFontCache.GetBrandLogoBitmap(64);

        public string GetRibbonLabel(Office.IRibbonControl control)
        {
            ProofingStyleMode activeMode = _proofingEngine?.StyleMode ?? _pendingStyleMode;
            return control.Id switch
            {
                "TabMyanmarExcelProofing" => UiLocalizer.Pick("Myanmar Spell Checker", "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး"),
                "GrpReviewMyanmarSpellChecker" => UiLocalizer.Pick("Myanmar Spell Checker", "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး"),
                "BtnReviewTogglePane" => UiLocalizer.Pick("Myanmar Spell Checker", "မြန်မာစာ စာစစ်ပြကွက်"),
                "BtnReviewScanNow" => UiLocalizer.Pick("Scan Sheet", "Sheet စစ်ဆေးမည်"),
                "GrpExcelScanAndFix" => UiLocalizer.Pick("Spreadsheet Proofing (Formula-Safe)", "ဇယားကွက် စစ်ဆေးရေး (Formula-Safe)"),
                "BtnScanActiveSheet" => UiLocalizer.Pick("Scan Active Sheet", "လက်ရှိ Sheet စစ်မည်"),
                "BtnScanSelection" => UiLocalizer.Pick("Scan Selection", "ရွေးကွက် စစ်မည်"),
                "BtnScanWorkbook" => UiLocalizer.Pick("Scan Workbook", "Workbook လုံး စစ်မည်"),
                "BtnFixAllIssues" => UiLocalizer.Pick("Review & Fix All", "အားလုံး တစ်ပြိုင်နက် ပြင်မည်"),
                "BtnUndoLastFix" => UiLocalizer.Pick("Undo Last Fix", "ပြန်ဆုတ်မည် (Undo)"),

                "GrpExcelStyleAndLive" => UiLocalizer.Pick("Style & Live Options", "အရေးအသားနှင့် အလိုအလျောက်စနစ်"),
                "MnuStyleMode" => activeMode switch
                {
                    ProofingStyleMode.ColloquialPermissive => UiLocalizer.Pick("Mode: Casual", "စနစ် - ပေါ့ပါးဟန်"),
                    ProofingStyleMode.FormalOfficial => UiLocalizer.Pick("Mode: Official", "စနစ် - ရုံးသုံးစာပေ"),
                    _ => UiLocalizer.Pick("Mode: Standard", "စနစ် - အထွေထွေစံ")
                },
                "ChkStyleCasual" => UiLocalizer.Pick("Casual / Chat (Colloquial)", "ပေါ့ပါးဟန် (Casual / Chat)"),
                "ChkStyleStandard" => UiLocalizer.Pick("General Standard", "အထွေထွေ စံသတ်ပုံ (Standard)"),
                "ChkStyleOfficial" => UiLocalizer.Pick("Official / Literary", "ရုံးသုံး / စာပေဟန် (Official)"),
                "BtnToggleFormalStyle" => UiLocalizer.Pick("Formal / Official Style", "စာပေ / ရုံးသုံး အရေးစကားစနစ်"),
                "BtnToggleLiveScan" => UiLocalizer.Pick("Live Auto-Scan", "အလိုအလျောက် စစ်ဆေးမှု (Live)"),
                "BtnToggleVisualUnderlines" => UiLocalizer.Pick("Show Underlines", "အနီမျဉ်း ပြသမှု (Underlines)"),
                "BtnClearExcelUnderlines" => UiLocalizer.Pick("Clear Marks", "မျဉ်းရှင်းမည်"),

                "GrpExcelDictionary" => UiLocalizer.Pick("Dictionary & Workspace (Offline)", "အဘိဓာန်နှင့် စစ်ဆေးရေးပြကွက် (Offline)"),
                "BtnSearchDictionary" => UiLocalizer.Pick("Dictionary & Encyclopedia", "သတ်ပုံကျမ်းနှင့် အဘိဓာန် ရှာရန်"),
                "BtnToggleTaskPane" => UiLocalizer.Pick("Show / Hide Task Pane", "စစ်ဆေးရေးပြကွက် ပြ/ဖျောက်"),
                "BtnManageCustomWords" => UiLocalizer.Pick("Custom Words", "မှတ်သားဝေါဟာရ စီမံရန်"),
                "BtnToggleUiLanguage" => UiLocalizer.Pick("UI: English ➔ မြန်မာ", "UI: မြန်မာ ➔ English"),
                "BtnLicenseActivation" => UiLocalizer.Pick("About & License (GPLv3)", "ဖန်တီးသူနှင့် လိုင်စင် (GPLv3)"),
                "BtnCheckUpdates" => MyanmarSpellChecker.Core.Updates.PluginUpdateService.IsNewerVersion(
                    MyanmarSpellChecker.Core.Updates.PluginUpdateService.LoadPreferences().AvailableCachedVersion,
                    MyanmarSpellChecker.Core.Updates.PluginUpdateService.GetCurrentInstalledVersion())
                    ? UiLocalizer.Pick("↑ Update Ready!", "↑ ဗားရှင်းအသစ် ရှိသည်!")
                    : UiLocalizer.Pick("Check Updates", "ဗားရှင်းအသစ် စစ်ရန်"),
                "MnuUserGuide" => UiLocalizer.Pick("User Guide", "အသုံးပြုပုံ လမ်းညွှန်"),
                "BtnGuideHtml" => UiLocalizer.Pick("Interactive Searchable Guide (HTML)", "အပြန်အလှန် ရှာဖွေဖတ်ရှုရန် လမ်းညွှန် (HTML)"),
                "BtnGuideDocx" => UiLocalizer.Pick("Printable A4 Manual (Word .docx)", "ပုံနှိပ်ရန်အသင့် A4 လမ်းညွှန်စာအုပ် (.docx)"),

                "GrpExcelTypography" => UiLocalizer.Pick("Font, PDF & Numerals", "ဖောင့်၊ PDF နှင့် ဂဏန်း ကိရိယာ"),
                "BtnConvertFonts" => "WinInnwa/Zawgyi ➔ Pyidaungsu",
                "BtnRepairBrokenPdf" => UiLocalizer.Pick("Repair Garbled PDF", "PDF စာသားပျက်ပြင်ရန်"),
                "BtnPasteCleanClipboard" => UiLocalizer.Pick("Paste Clean (PDF/ZG)", "သန့်စင်ပြီး ကူးထည့်ရန်"),
                "BtnNumeralsToMyanmar" => UiLocalizer.Pick("0-9 ➔ ၀-၉ (Text Cells)", "0-9 ➔ ၀-၉ (စာသားကွက်)"),
                "BtnCleanPunctuation" => UiLocalizer.Pick("Clean ၊ / ။ & ZWSP", "ပုဒ်ဖြတ် ၊ / ။ ရှင်းမည်"),

                "GrpExcelAccounting" => UiLocalizer.Pick("Accounting & Quality", "ဘဏ္ဍာရေးနှင့် အရည်အသွေး"),
                "BtnOpenToolkitPane" => UiLocalizer.Pick("Kyat Lakh & Calendar", "ကျပ်သိန်းနှင့် ပြက္ခဒိန်"),
                "BtnRewriteOfficialProse" => UiLocalizer.Pick("Convert to Formal Style", "ရုံးသုံးစာပေဟန် ပြောင်းမည်"),
                "BtnExportAuditReport" => UiLocalizer.Pick("Export Audit Report", "အရည်အသွေး အစီရင်ခံစာ"),
                _ => control.Id
            };
        }

        public string GetRibbonScreentip(Office.IRibbonControl control)
        {
            return control.Id switch
            {
                "BtnReviewTogglePane" => UiLocalizer.Pick(
                    "Show or hide the Myanmar Spell Checker task pane on the right",
                    "ညာဘက် မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး ပြကွက် (Task Pane) ကို ဖွင့်/ပိတ်ရန်"),
                "BtnReviewScanNow" => UiLocalizer.Pick(
                    "Scan all Myanmar text cells in the active Excel worksheet without touching formulas",
                    "လက်ရှိ Worksheet ရှိ မြန်မာစာ အကွက်အားလုံးကို Formula မထိခိုက်စေဘဲ စစ်ဆေးရန်"),
                "BtnScanActiveSheet" => UiLocalizer.Pick(
                    "Scan all Myanmar text cells in the active worksheet without touching Excel formulas",
                    "လက်ရှိ Worksheet ရှိ မြန်မာစာ အကွက်အားလုံးကို Formula မထိခိုက်စေဘဲ အမြန်စစ်ဆေးရန်"),
                "BtnScanSelection" => UiLocalizer.Pick(
                    "Scan only the currently selected cell range",
                    "ရွေးချယ်ထားသော အကွက်များ (Selected Range) ကိုသာ စစ်ဆေးရန်"),
                "BtnScanWorkbook" => UiLocalizer.Pick(
                    "Scan every worksheet in the active Excel workbook",
                    "Excel စာအုပ်တစ်ခုလုံးရှိ Sheet အားလုံးကို စစ်ဆေးရန်"),
                "BtnFixAllIssues" => UiLocalizer.Pick(
                    "Open the safe Fix All preview checklist to review and batch-apply replacements",
                    "သေချာသော သတ်ပုံနှင့် သဒ္ဒါအမှားအားလုံးကို Formula မထိခိုက်ဘဲ တစ်ပြိုင်နက် ပြင်ဆင်ရန်"),
                "BtnUndoLastFix" => UiLocalizer.Pick(
                    "Restore the cells modified by the most recent spell/toolkit action",
                    "နောက်ဆုံး ပြင်ဆင်ခဲ့သော အကွက်များကို မူလအတိုင်း ပြန်ဆုတ်ရန်"),
                "MnuStyleMode" => UiLocalizer.Pick(
                    "Choose writing register: Casual / Chat, General Standard, or Official / Literary",
                    "အရေးအသားစနစ် ရွေးချယ်ရန် — ပေါ့ပါးဟန် (Casual)၊ အထွေထွေစံ (Standard) သို့မဟုတ် ရုံးသုံးစာပေ (Official)"),
                "ChkStyleCasual" => UiLocalizer.Pick(
                    "Checks spelling & homophones while allowing conversational Burmese particles and shortcuts",
                    "စကားပြောဟန် အသုံးအနှုန်းများကို ခွင့်ပြုပြီး သတ်ပုံအမှားများကိုသာ စစ်ဆေးပေးပါမည်"),
                "ChkStyleStandard" => UiLocalizer.Pick(
                    "Checks official 462p orthography, homophones, and core grammar while allowing polite spoken particles",
                    "သတ်ပုံကျမ်း၊ အနက်တူအမှားနှင့် အခြေခံသဒ္ဒါများကို စစ်ဆေးပေးပါမည်"),
                "ChkStyleOfficial" => UiLocalizer.Pick(
                    "Enforces full formal literary and government ministry prose standards (ပါတယ် -> ပါသည်, တွေ -> များ)",
                    "ရုံးသုံးနှင့် စာပေအရေးအသား စံနှုန်းအပြည့်အဝ စစ်ဆေးပေးပါမည်"),
                "BtnToggleFormalStyle" => UiLocalizer.Pick(
                    "Toggle formal/literary Burmese checks (e.g., ပါတယ်➔ပါသည်, တွေ➔များ)",
                    "ရုံးသုံးနှင့် စာပေအရေးအသား (ပါတယ်➔ပါသည်၊ တွေ➔များ) စစ်ဆေးမှုကို ဖွင့်/ပိတ်ရန်"),
                "BtnToggleLiveScan" => UiLocalizer.Pick(
                    "Automatically check cells whenever you edit text and press Enter",
                    "အကွက်အတွင်း စာရိုက်ပြီး Enter နှိပ်တိုင်း အလိုအလျောက် စစ်ဆေးပေးရန်"),
                "BtnToggleVisualUnderlines" => UiLocalizer.Pick(
                    "Show/hide screen-only red, blue, and amber wavy underlines above cells (cell text color & .xlsx files are never modified)",
                    "ဖန်သားပြင်ပေါ်တွင် အနီ/အပြာ/လိမ္မော် မျဉ်းလှိုင်း ပြသမှုကို ဖွင့်/ပိတ်ရန် (Cell စာသားအရောင်နှင့် .xlsx ဖိုင်ကို လုံးဝ မပြောင်းလဲပါ)"),
                "BtnClearExcelUnderlines" => UiLocalizer.Pick(
                    "Immediately hide all screen-only proofing wavy underlines in the active workbook",
                    "ဖန်သားပြင်ပေါ်ရှိ စစ်ဆေးရေး မျဉ်းလှိုင်းအားလုံးကို ချက်ချင်းဖျောက်ရန်"),
                "BtnSearchDictionary" => UiLocalizer.Pick(
                    "Search the 100% offline Spelling Lexicon, Myanmar Dictionary & Encyclopedia (25,000+ entries)",
                    "သတ်ပုံကျမ်း၊ မြန်မာ-မြန်မာ အဘိဓာန် နှင့် စွယ်စုံဗဟုသုတ (၂၅,၀၀၀+) ရှာဖွေရန်"),
                "BtnToggleTaskPane" => UiLocalizer.Pick(
                    "Show or hide the Myanmar Spell Checker task pane on the right",
                    "ညာဘက် စစ်ဆေးရေးပြကွက် (Task Pane) ကို ဖွင့်/ပိတ် ပြုလုပ်ရန်"),
                "BtnManageCustomWords" => UiLocalizer.Pick(
                    "Manage personal Myanmar words shared across Word and Excel",
                    "Word နှင့် Excel မျှဝေသုံးစွဲသော ကိုယ်ပိုင်ဝေါဟာရများ စီမံရန်"),
                "BtnToggleUiLanguage" => UiLocalizer.FormatLanguageTooltip(),
                "BtnLicenseActivation" => UiLocalizer.Pick(
                    "View creator credits (Htet Yar Zar, Kyaukse University) and GNU GPL v3.0 open-source license",
                    "စနစ်အချက်အလက်၊ ဖန်တီးသူ ထက်ရာဇာ (ကျောက်ဆည်တက္ကသိုလ်) နှင့် GNU GPL v3.0 Open-Source လိုင်စင် ကြည့်ရှုရန်"),
                "BtnCheckUpdates" => UiLocalizer.Pick(
                    "Check GitHub Releases for new versions or update offline from a USB file (.exe / .zip / .db)",
                    "GitHub မှ ဗားရှင်းအသစ် စစ်ဆေးရန် သို့မဟုတ် အင်တာနက်မလိုဘဲ USB ဖိုင် (.exe / .zip / .db) မှ အဆင့်မြှင့်ရန်"),
                "MnuUserGuide" => UiLocalizer.Pick(
                    "Open the 100% offline Bilingual User Guide & Complete Tool Handbook (Interactive HTML or Printable A4 Word)",
                    "၁၀၀% အော့ဖ်လိုင်း အသုံးပြုပုံလမ်းညွှန်နှင့် ကိရိယာအညွှန်းစာအုပ် ဖွင့်ရန် (HTML သို့မဟုတ် A4 Word)"),
                "BtnGuideHtml" => UiLocalizer.Pick(
                    "Open the searchable offline HTML handbook with live EN / မြန်မာ language filter",
                    "ရှာဖွေနိုင်သော Offline HTML လမ်းညွှန် (EN / မြန်မာ) ကို ဖွင့်ရန်"),
                "BtnGuideDocx" => UiLocalizer.Pick(
                    "Open the print-ready ISO A4 Portrait Word manual (.docx)",
                    "A4 စာရွက်ဖြင့် ပရင့်ထုတ်နိုင်သော Word လမ်းညွှန်စာအုပ် (.docx) ကို ဖွင့်ရန်"),
                "BtnConvertFonts" => UiLocalizer.Pick(
                    "Convert legacy WinInnwa / Zawgyi text cells to Pyidaungsu Unicode without touching formulas",
                    "Formula နှင့် ဂဏန်းကွက်များကို မထိခိုက်ဘဲ Pyidaungsu Unicode သို့ ပြောင်းလဲရန်"),
                "BtnRepairBrokenPdf" => UiLocalizer.Pick(
                    "Reconstruct garbled Burmese PDF copy-paste across active sheet or selection without touching formulas (↶ Undo available)",
                    "Formula များကို မထိခိုက်ဘဲ Sheet သို့မဟုတ် ရွေးကွက်အတွင်းရှိ PDF စာသားပျက်များကို ပြန်လည်တည်ဆောက်ရန်"),
                "BtnPasteCleanClipboard" => UiLocalizer.Pick(
                    "Repair garbled PDF or Zawgyi text from Clipboard and paste into active cell(s) without overwriting formulas",
                    "Clipboard မှ PDF စာသားပျက် သို့မဟုတ် ဇော်ဂျီစာသားကို သန့်စင်၍ Formula မပျက်စေဘဲ ကူးထည့်ရန်"),
                "BtnNumeralsToMyanmar" => UiLocalizer.Pick(
                    "Convert ASCII digits (0-9) inside text cells to Myanmar digits (၀-၉) while protecting numeric/formula cells",
                    "တွက်ချက်မှု ကိန်းဂဏန်းကွက်နှင့် Formula များကို မထိခိုက်ဘဲ စာသားကွက်ရှိ ဂဏန်းများ ပြောင်းရန်"),
                "BtnCleanPunctuation" => UiLocalizer.Pick(
                    "Standardize Myanmar punctuation spacing (၊ / ။) and clean invisible ZWSP characters",
                    "ပုဒ်ထီး (၊)၊ ပုဒ်မ (။) အကွာအဝေးနှင့် ZWSP များကို သန့်စင်ရန်"),
                "BtnOpenToolkitPane" => UiLocalizer.Pick(
                    "Open the Kyat Lakh financial words converter, Myanmar calendar & batch spreadsheet tools",
                    "ကျပ်ငွေ (သိန်း) စာသားပြောင်းစက်၊ မြန်မာသက္ကရာဇ် ပြက္ခဒိန် နှင့် ဇယားကွက် ကိရိယာများ ဖွင့်ရန်"),
                "BtnRewriteOfficialProse" => UiLocalizer.Pick(
                    "Convert colloquial Burmese phrases into formal official prose",
                    "စကားပြောဟန် အသုံးအနှုန်းများကို ရုံးသုံးစာပေဟန်သို့ ပြောင်းလဲရန်"),
                "BtnExportAuditReport" => UiLocalizer.Pick(
                    "Generate a new Excel workbook containing a Myanmar text quality & spelling audit report",
                    "Excel မြန်မာစာ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ ထုတ်ယူရန်"),
                _ => string.Empty
            };
        }

        public void OnRibbonToggleLanguage(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleLanguage", ToggleUiLanguageFromUi);
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

        public bool GetStyleModeItemPressed(Office.IRibbonControl control)
        {
            ProofingStyleMode current = _proofingEngine?.StyleMode ?? _pendingStyleMode;
            return control?.Id switch
            {
                "ChkStyleCasual" => current == ProofingStyleMode.ColloquialPermissive,
                "ChkStyleStandard" => current == ProofingStyleMode.GeneralStandard,
                "ChkStyleOfficial" => current == ProofingStyleMode.FormalOfficial,
                _ => false
            };
        }

        public void OnRibbonSelectStyleMode(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonSelectStyleMode", () =>
            {
                ProofingStyleMode next = control?.Id switch
                {
                    "ChkStyleCasual" => ProofingStyleMode.ColloquialPermissive,
                    "ChkStyleOfficial" => ProofingStyleMode.FormalOfficial,
                    _ => ProofingStyleMode.GeneralStandard
                };
                ApplyStyleModeAndRescan(next);
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
                if (!pressed)
                {
                    _wavyOverlay?.HideOverlayImmediate();
                }

                _ribbonUi?.InvalidateControl("BtnToggleVisualUnderlines");
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.ShowToastBanner(
                    pressed
                        ? UiLocalizer.Pick(
                            "✓ Enabled screen-only red/blue/amber wavy underlines (cell text color & .xlsx files are never modified).",
                            "✓ ဖန်သားပြင်ပေါ်ရှိ အနီ/အပြာ/လိမ္မော် မျဉ်းလှိုင်း ပြသမှုကို ဖွင့်လိုက်ပါပြီ (Cell အရောင်နှင့် .xlsx ဖိုင် လုံးဝမပြောင်းလဲပါ)။")
                        : UiLocalizer.Pick(
                            "✓ Disabled screen-only proofing wavy underlines.",
                            "✓ ဖန်သားပြင်ပေါ်ရှိ စစ်ဆေးရေး မျဉ်းလှိုင်းပြသမှုကို ပိတ်လိုက်ပါပြီ။"));
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
                    if (pressed)
                    {
                        ScheduleStartupPaneWidthEnforcement(hwnd, entry.Pane, entry.Control, forceIfNarrow: true);
                    }
                }
                else if (pressed)
                {
                    GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                }
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
                _ribbonUi?.InvalidateControl("BtnReviewTogglePane");
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

        public void OnRibbonOpenHtmlGuide(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonOpenHtmlGuide", () => OfflineUserGuideExporter.OpenInteractiveHtmlGuide());
        }

        public void OnRibbonOpenWordGuide(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonOpenWordGuide", () => OfflineUserGuideExporter.OpenPrintableWordGuide());
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

        public void OnRibbonRepairBrokenPdf(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonRepairBrokenPdf", () =>
            {
                if (_scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                bool selOnly = false;
                try
                {
                    if (_excelApp?.Selection is Excel.Range sel && Convert.ToInt64(sel.CountLarge) > 1)
                        selOnly = true;
                }
                catch { }
                var res = _scannerToolkit.RepairBrokenPdfInScope(selectionOnly: selOnly);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonPasteCleanClipboard(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonPasteCleanClipboard", () =>
            {
                if (_scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.PasteAndRepairClipboardIntoActiveCell();
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
                ExecuteExportExcelProofingReport(ProofingReportExportFormat.PrintableA4WordDocument);
            });
        }

        private LicenseActivationForm? _activeAboutDialog;
        private PluginUpdateDialog? _activeUpdateDialog;

        public void OnRibbonCheckUpdates(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonCheckUpdates", ShowUpdateDialog);
        }

        private void ShowUpdateDialog()
        {
            if (_activeUpdateDialog != null && !_activeUpdateDialog.IsDisposed)
            {
                if (_activeUpdateDialog.WindowState == FormWindowState.Minimized)
                    _activeUpdateDialog.WindowState = FormWindowState.Normal;
                _activeUpdateDialog.BringToFront();
                _activeUpdateDialog.Activate();
                return;
            }

            int totalEntries = (_proofingEngine?.Trie.WordCount ?? 0) + (_proofingEngine?.Trie.ReferenceCount ?? 0);
            string keySnapshot = !string.IsNullOrWhiteSpace(_activeDatabaseKey)
                ? _activeDatabaseKey
                : AntiTamperGuard.ReconstructProtectedFallbackKey();

            var dlg = new PluginUpdateDialog(
                totalEntries,
                _databaseFilePath,
                keySnapshot,
                (freshTrie, appliedPath) =>
                {
                    if (_proofingEngine != null)
                    {
                        _proofingEngine.HotSwapTrie(freshTrie);
                        if (!string.IsNullOrWhiteSpace(_customDictFilePath))
                            _proofingEngine.LoadCustomDictionaryFile(_customDictFilePath);
                    }
                    SafeBeginInvokeUi(_uiMarshaller, () =>
                    {
                        try { _loadedDbLastWriteUtc = File.GetLastWriteTimeUtc(appliedPath); } catch { }
                        foreach (var kvp in _windowTaskPanes.Values)
                        {
                            kvp.Control.SetLexiconStats(
                                freshTrie.WordCount,
                                freshTrie.OfficialThatponCount,
                                freshTrie.RuleCount,
                                freshTrie.ReferenceCount,
                                freshTrie.EncyclopediaCount);
                        }
                    });
                },
                () => { try { _ribbonUi?.InvalidateControl("BtnCheckUpdates"); } catch { } });

            _activeUpdateDialog = dlg;
            dlg.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_activeUpdateDialog, dlg))
                    _activeUpdateDialog = null;
                try { dlg.Dispose(); } catch { }
            };
            dlg.Show();
        }

        private void ShowLicenseDialog()
        {
            if (_activeAboutDialog != null && !_activeAboutDialog.IsDisposed)
            {
                if (_activeAboutDialog.WindowState == FormWindowState.Minimized)
                    _activeAboutDialog.WindowState = FormWindowState.Normal;
                _activeAboutDialog.BringToFront();
                _activeAboutDialog.Activate();
                return;
            }

            int totalEntries = (_proofingEngine?.Trie.WordCount ?? 0) + (_proofingEngine?.Trie.ReferenceCount ?? 0);
            string resolvedPubKey = LicenseService.ResolvePublicKeyXml(_licenseFilePath, DefaultVendorPublicKeyXml);
            var dlg = new LicenseActivationForm(resolvedPubKey, _licenseFilePath, totalEntries, ShowUpdateDialog)
            {
                MinimizeBox = true,
                ShowInTaskbar = true
            };
            _activeAboutDialog = dlg;
            dlg.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_activeAboutDialog, dlg))
                    _activeAboutDialog = null;
                try { dlg.Dispose(); } catch { }
            };
            dlg.Show();
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

                foreach (int h in _paneWidthTimers.Keys.ToList())
                {
                    StopAndRemovePaneWidthTimer(h);
                }
                _settledPaneWidthHwnds.Clear();

                if (_excelApp != null)
                {
                    _excelApp.SheetChange -= OnSheetChange;
                    _excelApp.SheetSelectionChange -= OnSheetSelectionChange;
                    _excelApp.SheetBeforeRightClick -= OnSheetBeforeRightClick;
                    _excelApp.SheetBeforeDoubleClick -= OnSheetBeforeDoubleClick;
                    _excelApp.SheetActivate -= OnSheetActivate;
                    _excelApp.WindowActivate -= OnWindowActivate;
                    _excelApp.WindowDeactivate -= OnWindowDeactivate;
                    _excelApp.WorkbookOpen -= OnWorkbookOpen;
                    ((Excel.AppEvents_Event)_excelApp).NewWorkbook -= OnNewWorkbook;
                    _excelApp.WorkbookBeforeSave -= OnWorkbookBeforeSave;
                    _excelApp.WorkbookBeforePrint -= OnWorkbookBeforePrint;
                    _excelApp.WorkbookBeforeClose -= OnWorkbookBeforeClose;
                }

                CloseActiveExcelReviewDialog();
                _quickFixController?.Dispose();
                _quickFixController = null;

                _wavyOverlay?.Dispose();
                _wavyOverlay = null;

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

