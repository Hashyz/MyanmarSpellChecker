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
using MyanmarSpellChecker.Core.Presentation;
using MyanmarSpellChecker.Core.Security;
using MyanmarSpellChecker.PowerPointAddIn.Interop;
using MyanmarSpellChecker.PowerPointAddIn.UI;
using Office = Microsoft.Office.Core;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace MyanmarSpellChecker.PowerPointAddIn
{
    /// <summary>
    /// Enterprise COM Add-in Entry Point for Microsoft PowerPoint (Desktop Windows, 32-bit &amp; 64-bit AnyCPU).
    /// Shares the exact same AES-256 + HMAC-SHA256 encrypted dictionary (mlc_thatpon.db), style mode,
    /// custom words, and offline RSA-2048 license as WordAddIn and ExcelAddIn while enforcing:
    /// - 100% Zero-Formatting-Mutation Slide Proofing (Win32 click-through wavy overlay via TextRange screen bounds),
    /// - Recursive Slide Shapes + Tables + Nested GroupItems + Speaker Notes (+ optional Slide Masters),
    /// - Automatic Slide Show (F5 / Presenter View) suspension, and
    /// - Surgical 1-based TextRange.Characters edits + 30-step Presentation Undo.
    /// </summary>
    [ComVisible(true)]
    [Guid("A7C5E964-8F33-4D9B-BE12-9D34A7F16C55")]
    [ProgId("MyanmarSpellChecker.PowerPointAddIn")]
    public class PowerPointThisAddIn : IDTExtensibility2, Office.IRibbonExtensibility, Office.ICustomTaskPaneConsumer
    {
        public const string DefaultSqlCipherKey = "MM-GOV-THATPON-SQLCIPHER-2026!";

        public const string DefaultVendorPublicKeyXml = LicenseService.DefaultEmbeddedPublicKeyXml;

        private static readonly object s_instanceGate = new object();
        private static bool s_primaryInstanceActive;

        private bool _isPrimaryInstance;
        private PowerPoint.Application? _pptApp;
        private Office.IRibbonUI? _ribbonUi;
        private Office.ICTPFactory? _ctpFactory;
        private Control? _uiMarshaller;
        private Timer? _startupTaskPaneTimer;
        private int _startupTaskPaneRetries;

        private MyanmarProofingEngine? _proofingEngine;
        private volatile MyanmarProofingEngine? _readyEngineFromWarmup;
        private volatile string? _warmupErrorSummary;
        private volatile bool _warmupCompleted;
        private PresentationScanScope? _queuedScanAfterWarmup;
        private Timer? _uiEngineAttachTimer;
        private PowerPointDeckScannerAndToolkit? _scannerToolkit;
        private PowerPointQuickFixController? _quickFixController;
        private PowerPointWavyUnderlineOverlay? _wavyOverlay;
        private ProofingStyleMode _pendingStyleMode = ProofingStyleMode.GeneralStandard;
        private bool _pendingLiveAutoScan = true;
        private bool _pendingVisualUnderlines = true;
        private bool _pendingIncludeMasters;

        // Per-Window Task Pane map (keyed by PowerPoint.DocumentWindow.HWND) for Multi-Presentation SDI support
        private readonly Dictionary<int, (Office.CustomTaskPane Pane, PowerPointProofingTaskPaneControl Control)> _windowTaskPanes =
            new Dictionary<int, (Office.CustomTaskPane Pane, PowerPointProofingTaskPaneControl Control)>();
        private readonly HashSet<int> _settledPaneWidthHwnds = new HashSet<int>();
        private readonly Dictionary<int, Timer> _paneWidthTimers = new Dictionary<int, Timer>();

        private string _appDataFolder = string.Empty;
        private string _uiLanguageConfigPath = string.Empty;
        private string _styleModeConfigPath = string.Empty;
        private string _ecoModeConfigPath = string.Empty;
        private string _visualUnderlineConfigPath = string.Empty;
        private string _includeMastersConfigPath = string.Empty;
        private string _customDictFilePath = string.Empty;
        private string _licenseFilePath = string.Empty;
        private string _databaseFilePath = string.Empty;
        private string _activeDatabaseKey = string.Empty;
        private DateTime _loadedDbLastWriteUtc = DateTime.MinValue;
        private int _dbHotReloadInProgress;
        private FixAllPreviewDialog<PresentationSlideIssue>? _activeReviewDialog;
        private string _activeReviewPresentationName = string.Empty;
        private IssueFilterMode _activeReviewFilterMode = IssueFilterMode.All;
        private readonly Dictionary<string, List<ProofingSessionFixedRecord>> _sessionFixedByPresentation =
            new Dictionary<string, List<ProofingSessionFixedRecord>>(StringComparer.OrdinalIgnoreCase);

        static PowerPointThisAddIn()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var asmName = new System.Reflection.AssemblyName(args.Name);
                    foreach (string dir in GetCandidateAddInDirectories(typeof(PowerPointThisAddIn).Assembly))
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

            _pptApp = application as PowerPoint.Application;
            if (_pptApp == null)
                return;

            AddInDiagnostics.SafeExecute("OnConnection", () =>
            {
                _uiMarshaller = new Control();
                IntPtr _ = _uiMarshaller.Handle;

                InitializePathsAndStartAsyncEngineLoad();
                HookPowerPointEvents();
            });
        }

        private void InitializePathsAndStartAsyncEngineLoad()
        {
            List<string> addInDirs = GetCandidateAddInDirectories(typeof(PowerPointThisAddIn).Assembly);
            string? installDir = addInDirs.Count > 0 ? addInDirs[0] : Path.GetDirectoryName(typeof(PowerPointThisAddIn).Assembly.Location);
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
            _visualUnderlineConfigPath = Path.Combine(localAppDataDir, "powerpoint_underlines.txt");
            _includeMastersConfigPath = Path.Combine(localAppDataDir, "powerpoint_include_masters.txt");
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

            var candidateDbs = rawDbPaths
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Select(p => Path.GetFullPath(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.Length >= 5_000_000 ? 2 : (fi.Length >= 100_000 ? 1 : 0))
                .ThenByDescending(fi => fi.LastWriteTimeUtc)
                .ThenByDescending(fi => fi.Length)
                .ToList();

            // Always fall back to writable per-user %LocalAppData% (never read-only C:\ProgramData) if no pre-deployed DB was found
            _databaseFilePath = candidateDbs.Count > 0
                ? candidateDbs[0].FullName
                : localAppDb;

            _pendingStyleMode = LoadSavedStyleMode();
            _pendingVisualUnderlines = LoadSavedBoolSetting(_visualUnderlineConfigPath, defaultVal: true);
            _pendingIncludeMasters = LoadSavedBoolSetting(_includeMastersConfigPath, defaultVal: false);

            string dbPathSnapshot = _databaseFilePath;
            string localAppDbSnapshot = localAppDb;
            string licPathSnapshot = _licenseFilePath;
            string userDictSnapshot = _customDictFilePath;
            ProofingStyleMode initialMode = _pendingStyleMode;

            Task.Run(() =>
            {
                try
                {
                    SuppressOfficeMissingBurmeseBannerRegistry();

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

                    _ = engine.CheckText("မြန်မာစာ ပီးပီ။");

                    // Publish ready engine BEFORE UI marshaling so self-healing UI-thread checkpoints attach even if _uiMarshaller was created on an MTA preloader thread!
                    _activeDatabaseKey = activeDbKey;
                    _loadedDbLastWriteUtc = loadedStampUtc;
                    _readyEngineFromWarmup = engine;
                    _warmupCompleted = true;

                    Control? paneCtrl = null;
                    try { paneCtrl = _windowTaskPanes.Values.FirstOrDefault().Control; } catch { }
                    Control? marshaller = (paneCtrl != null && paneCtrl.IsHandleCreated && !paneCtrl.IsDisposed)
                        ? paneCtrl
                        : _uiMarshaller;

                    if (marshaller != null && marshaller.IsHandleCreated && !marshaller.IsDisposed)
                    {
                        try
                        {
                            marshaller.BeginInvoke(new Action(() =>
                            {
                                AddInDiagnostics.SafeExecute("AttachEngineOnStaThread", () => AttachEngineOnStaThread(engine));
                            }));
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _warmupErrorSummary = ex.Message;
                    _warmupCompleted = true;
                    AddInDiagnostics.LogException("AsyncEngineWarmup", ex);
                }
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

        private bool EnsureEngineAttachedOnUiThread(bool showFeedbackIfNotReady = false, PresentationScanScope? requestedScope = null)
        {
            if (_proofingEngine != null && _scannerToolkit != null)
                return true;

            MyanmarProofingEngine? ready = _readyEngineFromWarmup;
            if (ready != null)
            {
                AddInDiagnostics.SafeExecute("EnsureEngineAttachedOnUiThread", () => AttachEngineOnStaThread(ready));
                return _proofingEngine != null && _scannerToolkit != null;
            }

            if (!_warmupCompleted)
            {
                if (requestedScope.HasValue)
                    _queuedScanAfterWarmup = requestedScope.Value;

                if (showFeedbackIfNotReady)
                {
                    var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                    ctrl?.ShowToastBanner(UiLocalizer.Pick(
                        "⏳ Preparing offline Myanmar dictionary (1–2 seconds)... your action will run automatically!",
                        "⏳ မြန်မာအဘိဓာန် အသင့်ပြင်နေပါသည် (၁-၂ စက္ကန့်)... ပြီးသည်နှင့် အလိုအလျောက် စစ်ဆေးပေးပါမည်။"));
                }
                return false;
            }

            // Synchronous emergency recovery on UI thread if background warmup failed earlier
            try
            {
                if (!string.IsNullOrEmpty(_databaseFilePath) && File.Exists(_databaseFilePath))
                {
                    string key = !string.IsNullOrWhiteSpace(_activeDatabaseKey)
                        ? _activeDatabaseKey
                        : AntiTamperGuard.ReconstructProtectedFallbackKey();
                    var repo = new EncryptedDictionaryRepository(_databaseFilePath);
                    MyanmarTrie trie = repo.LoadIntoTrie(key);
                    var recovered = new MyanmarProofingEngine(trie, _pendingStyleMode);
                    recovered.LoadCustomDictionaryFile(_customDictFilePath);
                    _activeDatabaseKey = key;
                    _loadedDbLastWriteUtc = File.GetLastWriteTimeUtc(_databaseFilePath);
                    _readyEngineFromWarmup = recovered;
                    AttachEngineOnStaThread(recovered);
                    if (_proofingEngine != null && _scannerToolkit != null)
                        return true;
                }
            }
            catch (Exception ex)
            {
                _warmupErrorSummary = ex.Message;
                AddInDiagnostics.LogException("EmergencySyncEngineLoad", ex);
            }

            if (showFeedbackIfNotReady)
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                string detail = string.IsNullOrWhiteSpace(_warmupErrorSummary) ? _databaseFilePath : _warmupErrorSummary!;
                ctrl?.ShowToastBanner(
                    UiLocalizer.Pick(
                        $"⚠ Offline dictionary (mlc_thatpon.db) could not be loaded ({detail}). Please re-run Install-EasyUser.bat.",
                        $"⚠ အဘိဓာန်ဖိုင် (mlc_thatpon.db) ဖတ်ရှု၍မရပါ ({detail})။ Install-EasyUser.bat ကို ပြန်လည် Run ပေးပါ။"),
                    isSuccess: false);
            }
            return false;
        }

        private void AttachEngineOnStaThread(MyanmarProofingEngine engine)
        {
            if (!_isPrimaryInstance || _pptApp == null)
                return;
            if (_proofingEngine != null && _scannerToolkit != null)
                return;

            engine.StyleMode = _pendingStyleMode;
            _proofingEngine = engine;

            _scannerToolkit = new PowerPointDeckScannerAndToolkit(_pptApp, _proofingEngine)
            {
                IsLiveAutoScanEnabled = _pendingLiveAutoScan,
                IsVisualUnderlineEnabled = _pendingVisualUnderlines,
                IncludeSlideMasters = _pendingIncludeMasters
            };

            _quickFixController = new PowerPointQuickFixController(
                _pptApp,
                _scannerToolkit,
                ApplySingleIssueFixFromUi,
                ApplySameWordBatchFixFromUi,
                IgnoreIssueFromUi,
                AddIssueWordToDictionaryFromUi,
                item => GetOrCreateActiveWindowTaskPaneControl(autoShow: false)?.HighlightCellIssueFromSheet(item),
                () => _wavyOverlay?.NotifyPopupBoundsChanged());

            // Wire scanner events BEFORE creating layered overlay so RDP / display-driver quirks never break Task Pane scanning!
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
                    ctrl.SetScanOutcomeMetadata(_scannerToolkit.GetLastScanMetadataForActivePresentation());
                    ctrl.RenderIssues(items);
                }
                if (_activeReviewDialog != null && !_activeReviewDialog.IsDisposed)
                {
                    try
                    {
                        var freshEntries = BuildPowerPointFixPreviewEntries(_activeReviewFilterMode);
                        _activeReviewDialog.ReloadEntriesFromHost(freshEntries);
                    }
                    catch { }
                }
            };
            _scannerToolkit.UndoStateChanged += SyncUndoStateAcrossTaskPanes;
            _scannerToolkit.RefreshEcoActiveState();

            try
            {
                _wavyOverlay = new PowerPointWavyUnderlineOverlay(
                    _pptApp,
                    _scannerToolkit,
                    () => _quickFixController?.ActivePopupScreenBounds);
                IntPtr _ = _wavyOverlay.Handle;
            }
            catch (Exception overlayEx)
            {
                AddInDiagnostics.LogException("PowerPointWavyUnderlineOverlayInit", overlayEx);
                _wavyOverlay = null;
            }

            foreach (var kvp in _windowTaskPanes.Values)
            {
                kvp.Control.SetLexiconStats(
                    _proofingEngine.Trie.WordCount,
                    _proofingEngine.Trie.OfficialThatponCount,
                    _proofingEngine.Trie.RuleCount,
                    _proofingEngine.Trie.ReferenceCount,
                    _proofingEngine.Trie.EncyclopediaCount);
                kvp.Control.SetStyleMode(_proofingEngine.StyleMode);
                kvp.Control.SetIncludeMastersState(_scannerToolkit.IncludeSlideMasters);
                kvp.Control.SetEcoModeState(EcoPerformanceGovernor.CurrentPreference, _scannerToolkit.IsEcoActive);
                kvp.Control.RefreshActiveTabAfterEngineReady();
            }

            _ribbonUi?.Invalidate();

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

            try
            {
                if (_queuedScanAfterWarmup.HasValue)
                {
                    PresentationScanScope queued = _queuedScanAfterWarmup.Value;
                    _queuedScanAfterWarmup = null;
                    if (PowerPointDeckScannerAndToolkit.IsPowerPointReadyForInterop(_pptApp))
                    {
                        _scannerToolkit.RunScan(queued);
                    }
                }
                else if (PowerPointDeckScannerAndToolkit.IsPowerPointReadyForInterop(_pptApp))
                {
                    _scannerToolkit.QueueLiveActiveSlideScan();
                }
            }
            catch { }
        }

        private void HookPowerPointEvents()
        {
            if (_pptApp == null)
                return;

            // Subscribe each dispinterface event independently so older Office / PIA versions missing newer events never abort the rest!
            try { _pptApp.WindowSelectionChange += OnWindowSelectionChange; } catch { }
            try { _pptApp.WindowActivate += OnWindowActivate; } catch { }
            try { _pptApp.WindowDeactivate += OnWindowDeactivate; } catch { }
            try { _pptApp.PresentationOpen += OnPresentationOpen; } catch { }
            try { _pptApp.AfterNewPresentation += OnAfterNewPresentation; } catch { }
            try { _pptApp.PresentationNewSlide += _ => OnWindowSelectionChange(null!); } catch { }
            try { _pptApp.PresentationClose += OnPresentationClose; } catch { }
            try { _pptApp.SlideShowBegin += OnSlideShowBegin; } catch { }
            try { _pptApp.SlideShowEnd += OnSlideShowEnd; } catch { }
        }

        private void OnWindowSelectionChange(PowerPoint.Selection sel)
        {
            AddInDiagnostics.SafeExecute("OnWindowSelectionChange", () =>
            {
                EnsureEngineAttachedOnUiThread();
                if (_scannerToolkit == null || _scannerToolkit.IsSlideShowRunning)
                    return;

                _scannerToolkit.QueueLiveActiveSlideScan();
                _quickFixController?.ScheduleCheckForActiveTextSelection(160);
            });
        }

        private void OnSlideShowBegin(PowerPoint.SlideShowWindow wn)
        {
            AddInDiagnostics.SafeExecute("OnSlideShowBegin", () =>
            {
                _quickFixController?.CloseActivePopup();
                _scannerToolkit?.SetSlideShowState(true);
                _wavyOverlay?.HideOverlayImmediate();
            });
        }

        private void OnSlideShowEnd(PowerPoint.Presentation pres)
        {
            AddInDiagnostics.SafeExecute("OnSlideShowEnd", () =>
            {
                _scannerToolkit?.SetSlideShowState(false);
                _scannerToolkit?.NotifyVisualUnderlinesInvalidated();
            });
        }

        private void OnWindowDeactivate(PowerPoint.Presentation pres, PowerPoint.DocumentWindow wn)
        {
            AddInDiagnostics.SafeExecute("OnWindowDeactivate", () =>
            {
                _quickFixController?.CloseActivePopup();
                _wavyOverlay?.HideOverlayImmediate();
            });
        }

        private void OnWindowActivate(PowerPoint.Presentation pres, PowerPoint.DocumentWindow wn)
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

                EnsureEngineAttachedOnUiThread();
                _scannerToolkit?.RefreshEcoActiveState();
                _scannerToolkit?.NotifyVisualUnderlinesInvalidated();
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                int winHwnd = ResolveWindowPaneSlotId(wn);
                if (winHwnd == 0)
                {
                    PowerPoint.DocumentWindow? aw = null;
                    try { aw = _pptApp?.ActiveWindow; } catch { }
                    winHwnd = ResolveWindowPaneSlotId(aw);
                }
                if (winHwnd != 0 && _windowTaskPanes.TryGetValue(winHwnd, out var paneEntry) && !_settledPaneWidthHwnds.Contains(winHwnd))
                {
                    ScheduleStartupPaneWidthEnforcement(winHwnd, paneEntry.Pane, paneEntry.Control, forceIfNarrow: false);
                }

                if (ctrl != null && _scannerToolkit != null)
                {
                    ctrl.SetEcoModeState(EcoPerformanceGovernor.CurrentPreference, _scannerToolkit.IsEcoActive);
                    ctrl.SetIncludeMastersState(_scannerToolkit.IncludeSlideMasters);
                    if (!_scannerToolkit.IsScanning)
                    {
                        ctrl.SetScanOutcomeMetadata(_scannerToolkit.GetLastScanMetadataForActivePresentation());
                        ctrl.RenderIssues(_scannerToolkit.GetCachedIssuesForActivePresentation());
                    }
                    ctrl.SetUndoState(_scannerToolkit.UndoManager.UndoCount, _scannerToolkit.UndoManager.LatestDescription);
                }
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
                _ribbonUi?.InvalidateControl("BtnReviewTogglePane");
            });
        }

        private void OnPresentationOpen(PowerPoint.Presentation pres)
        {
            AddInDiagnostics.SafeExecute("OnPresentationOpen", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                EnforceActiveWindowStartupPaneWidth(forceIfNarrow: false);
                _scannerToolkit?.QueueLiveActiveSlideScan();
            });
        }

        private void OnAfterNewPresentation(PowerPoint.Presentation pres)
        {
            AddInDiagnostics.SafeExecute("OnAfterNewPresentation", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                EnforceActiveWindowStartupPaneWidth(forceIfNarrow: false);
            });
        }

        private void OnPresentationClose(PowerPoint.Presentation pres)
        {
            AddInDiagnostics.SafeExecute("OnPresentationClose", () =>
            {
                _quickFixController?.CloseActivePopup();
                _wavyOverlay?.HideOverlayImmediate();

                string presKey = string.Empty;
                string presName = string.Empty;
                try
                {
                    presName = pres?.Name ?? string.Empty;
                    presKey = !string.IsNullOrWhiteSpace(pres?.FullName) ? pres!.FullName : presName;
                }
                catch { }

                if (!string.IsNullOrEmpty(presName) && string.Equals(presName, _activeReviewPresentationName, StringComparison.OrdinalIgnoreCase))
                {
                    CloseActivePowerPointReviewDialog();
                }
                if (!string.IsNullOrEmpty(presName))
                {
                    _sessionFixedByPresentation.Remove(presName);
                }
                if (!string.IsNullOrEmpty(presKey))
                {
                    _scannerToolkit?.ClearCachedPresentationState(presKey);
                }
                PruneClosedWindowTaskPanes();

                try
                {
                    if ((_pptApp?.Presentations?.Count ?? 0) <= 1)
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

        private const int FallbackPaneSlotId = -1;

        private int ResolveWindowPaneSlotId(PowerPoint.DocumentWindow? win)
        {
            if (win != null)
            {
                try
                {
                    int raw = win.HWND;
                    if (raw != 0)
                        return raw;
                }
                catch
                {
                    // DocumentWindow.HWND is FRestricted in PowerPoint TypeLib and can throw on certain Office builds / x64 handles
                }
                return FallbackPaneSlotId;
            }

            if (HasOpenPresentation())
                return FallbackPaneSlotId;

            return 0;
        }

        private void PruneClosedWindowTaskPanes()
        {
            if (_pptApp == null || _windowTaskPanes.Count == 0)
                return;

            try
            {
                if (!HasOpenPresentation())
                {
                    var allKeys = _windowTaskPanes.Keys.ToList();
                    foreach (int h in allKeys)
                    {
                        _windowTaskPanes.Remove(h);
                        _settledPaneWidthHwnds.Remove(h);
                        StopAndRemovePaneWidthTimer(h);
                    }
                    return;
                }

                var liveHwnds = new HashSet<int> { FallbackPaneSlotId };
                foreach (PowerPoint.DocumentWindow win in _pptApp.Windows)
                {
                    try
                    {
                        int id = ResolveWindowPaneSlotId(win);
                        if (id != 0)
                            liveHwnds.Add(id);
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

        private bool HasOpenPresentation()
        {
            try
            {
                return _pptApp != null && _pptApp.Presentations != null && _pptApp.Presentations.Count > 0;
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
                PowerPoint.DocumentWindow? win = null;
                try { win = _pptApp?.ActiveWindow; } catch { }
                int slotId = ResolveWindowPaneSlotId(win);
                if (slotId != 0 && _windowTaskPanes.TryGetValue(slotId, out var entry))
                {
                    ScheduleStartupPaneWidthEnforcement(slotId, entry.Pane, entry.Control, forceIfNarrow);
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
                EnsureEngineAttachedOnUiThread();
                StartUiThreadEngineAttachWatchdog();
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            });
        }

        private void StartUiThreadEngineAttachWatchdog()
        {
            if (_proofingEngine != null && _scannerToolkit != null)
                return;
            if (_uiEngineAttachTimer != null)
                return;

            int ticks = 0;
            _uiEngineAttachTimer = new Timer { Interval = 300 };
            _uiEngineAttachTimer.Tick += (s, e) =>
            {
                ticks++;
                try
                {
                    if (EnsureEngineAttachedOnUiThread() || ticks >= 60)
                    {
                        _uiEngineAttachTimer?.Stop();
                        _uiEngineAttachTimer?.Dispose();
                        _uiEngineAttachTimer = null;
                    }
                }
                catch { }
            };
            _uiEngineAttachTimer.Start();
        }

        private PowerPointProofingTaskPaneControl? GetOrCreateActiveWindowTaskPaneControl(bool autoShow)
        {
            if (!_isPrimaryInstance || _ctpFactory == null || _pptApp == null)
                return null;

            try
            {
                EnsureEngineAttachedOnUiThread();

                PowerPoint.DocumentWindow? activeWin = null;
                try { activeWin = _pptApp.ActiveWindow; } catch { }
                int hwnd = ResolveWindowPaneSlotId(activeWin);

                if (hwnd == 0)
                {
                    // If user explicitly clicked Show/Open on the Ribbon while in Protected View or empty shell, still create global pane if possible
                    if (!autoShow)
                        return null;

                    if (!PowerPointDeckScannerAndToolkit.IsProtectedViewOnly(_pptApp))
                    {
                        ScheduleDeferredTaskPaneCreation();
                        return null;
                    }
                    hwnd = FallbackPaneSlotId;
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

                string paneTitle = UiLocalizer.Pick("Myanmar Spell Checker", "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး");
                Office.CustomTaskPane pane;
                try
                {
                    pane = activeWin != null
                        ? _ctpFactory.CreateCTP("MyanmarSpellChecker.PowerPointTaskPaneControl", paneTitle, activeWin)
                        : _ctpFactory.CreateCTP("MyanmarSpellChecker.PowerPointTaskPaneControl", paneTitle, Type.Missing);
                }
                catch
                {
                    // Fallback for PowerPoint 2013/2016/LTSC builds that reject DocumentWindow as ctpParentWindow
                    pane = _ctpFactory.CreateCTP("MyanmarSpellChecker.PowerPointTaskPaneControl", paneTitle, Type.Missing);
                }

                var ctrl = (PowerPointProofingTaskPaneControl)pane.ContentControl;
                StartUiThreadEngineAttachWatchdog();
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
                    ctrl.SetIncludeMastersState(_scannerToolkit.IncludeSlideMasters);
                    ctrl.SetUndoState(_scannerToolkit.UndoManager.UndoCount, _scannerToolkit.UndoManager.LatestDescription);
                }
                else
                {
                    ctrl.SetIncludeMastersState(_pendingIncludeMasters);
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
            PowerPointProofingTaskPaneControl ctrl,
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
            PowerPointProofingTaskPaneControl ctrl,
            bool forceIfNarrow)
        {
            try
            {
                if (!pane.Visible)
                    return;

                IntPtr hostPtr = winHwnd > 0
                    ? PowerPointSlideGeometryHelper.ToSafeHwnd(winHwnd)
                    : PowerPointSlideGeometryHelper.TryGetDocumentWindowHwnd(_pptApp);
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

                    if (HasOpenPresentation() &&
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
                EnsureEngineAttachedOnUiThread();
                PowerPoint.DocumentWindow? win = null;
                try { win = _pptApp?.ActiveWindow; } catch { }
                int slotId = ResolveWindowPaneSlotId(win);
                if (slotId != 0 || _startupTaskPaneRetries >= 40)
                {
                    _startupTaskPaneTimer?.Stop();
                    _startupTaskPaneTimer?.Dispose();
                    _startupTaskPaneTimer = null;

                    if (slotId != 0)
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
                    "● Eco Mode: ON — minimizes CPU & RAM usage for smooth slide editing.",
                    "● ပေါ့ပါးစနစ် (Eco Mode: ON) — CPU နှင့် RAM အနည်းဆုံးဖြင့် စက်မလေးစေဘဲ စစ်ဆေးပေးပါမည်။"),
                EcoModePreference.Off => UiLocalizer.Pick(
                    "○ Eco Mode: OFF (Fast Mode) — running at maximum multi-core performance.",
                    "○ ပေါ့ပါးစနစ် ပိတ်ထားသည် (Fast Mode) — စွမ်းဆောင်ရည်အပြည့်ဖြင့် စစ်ဆေးပါမည်။"),
                _ => UiLocalizer.Pick(
                    "◐ Eco Mode: Auto — smartly balances fast slide scanning with large-deck protection.",
                    "◐ ပေါ့ပါးစနစ် (Auto) — စက်အားနည်းလျှင် သို့မဟုတ် ဆလိုက်များလျှင် အလိုအလျောက် ချိန်ညှိစစ်ဆေးပေးပါမည်။")
            };
            ctrl?.ShowToastBanner(toast);
        }

        private void SetIncludeSlideMastersFromUi(bool includeMasters)
        {
            _pendingIncludeMasters = includeMasters;
            try
            {
                if (!string.IsNullOrEmpty(_includeMastersConfigPath))
                    File.WriteAllText(_includeMastersConfigPath, includeMasters.ToString());
            }
            catch { }

            if (_scannerToolkit != null)
                _scannerToolkit.IncludeSlideMasters = includeMasters;

            foreach (var kv in _windowTaskPanes.Values)
            {
                try { kv.Control.SetIncludeMastersState(includeMasters); } catch { }
            }
            _ribbonUi?.InvalidateControl("BtnToggleIncludeMasters");

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
            ctrl?.ShowToastBanner(
                includeMasters
                    ? UiLocalizer.Pick(
                        "✓ Slide Masters & Custom Layouts will now be included when scanning All Slides.",
                        "✓ Slide အားလုံး စစ်ဆေးရာတွင် Slide Master နှင့် Layout ပုံစံခွက်များကိုပါ ထည့်သွင်းစစ်ဆေးပါမည်။")
                    : UiLocalizer.Pick(
                        "✓ Slide Masters & Custom Layouts excluded (scanning visible slides & speaker notes only).",
                        "✓ Slide Master ပုံစံခွက်များကို ချန်လှပ်ထားပါမည် (ဆလိုက်နှင့် Speaker Notes များကိုသာ စစ်ဆေးမည်)။"));

            if (_scannerToolkit != null && _scannerToolkit.CurrentScope == PresentationScanScope.EntirePresentation)
            {
                TriggerScanWithDbHotReload(PresentationScanScope.EntirePresentation);
            }
        }

        private void WireTaskPaneEvents(PowerPointProofingTaskPaneControl ctrl)
        {
            ctrl.RequestScanScope += scope => TriggerScanWithDbHotReload(scope);
            ctrl.RequestToggleIncludeMasters += SetIncludeSlideMastersFromUi;
            ctrl.RequestCancelScan += () => _scannerToolkit?.CancelActiveScan();
            ctrl.RequestToggleLanguage += ToggleUiLanguageFromUi;
            ctrl.RequestCycleEcoMode += CycleEcoModeFromUi;
            ctrl.RequestSetEcoMode += ApplyEcoModePreferenceFromUi;
            ctrl.RequestFixAllIssues += ExecuteFixAllInActiveScope;
            ctrl.RequestUndoLastAction += ExecuteUndoLastAction;
            ctrl.RequestApplyDockedBatch += approved => ApplyPowerPointApprovedBatch(ctrl, approved, GetPowerPointFilterScopeLabel(_activeReviewFilterMode));
            ctrl.RequestPopOutDockedChecklist += entries =>
                OpenOrUpdatePowerPointModelessReviewStudio(_activeReviewFilterMode, ctrl, entries, forceFloating: true);
            ctrl.RequestClearUnderlines += ExecuteClearPowerPointUnderlines;
            ctrl.RequestLicenseDialog += ShowLicenseDialog;
            ctrl.RequestChangeStyleMode += ApplyStyleModeAndRescan;
            ctrl.RequestSelectIssueInSheet += item => _scannerToolkit?.NavigateToIssue(item);

            ctrl.RequestApplySuggestion += ApplySingleIssueFixFromUi;
            ctrl.RequestApplyBatchSuggestion += ApplySameWordBatchFixFromUi;
            ctrl.RequestIgnoreOnce += IgnoreIssueFromUi;
            ctrl.RequestAddToDictionary += AddIssueWordToDictionaryFromUi;

            ctrl.RequestDictionarySearch += query =>
            {
                EnsureEngineAttachedOnUiThread();
                return _proofingEngine?.SearchDictionary(query, 45) ?? new List<DictionaryEntry>();
            };
            ctrl.RequestIsEngineReady += () =>
            {
                EnsureEngineAttachedOnUiThread();
                return _proofingEngine != null;
            };
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
                _scannerToolkit?.TryGetSelectedSlideText(80) ?? string.Empty;
            ctrl.RequestGetSelectedNumericText += () =>
                _scannerToolkit?.TryGetSelectedNumericText() ?? string.Empty;
            ctrl.RequestInsertTextIntoCell += text =>
            {
                if (_scannerToolkit == null)
                    return;
                bool ok = _scannerToolkit.InsertTextIntoActiveSlideSelection(text);
                ctrl.ShowToastBanner(
                    ok
                        ? UiLocalizer.Pick(
                            "✓ Inserted text into slide selection (or copied to Clipboard if no text box was active).",
                            "✓ ဆလိုက်အတွင်း စာသားထည့်သွင်းပြီးပါပြီ (သို့မဟုတ် Clipboard သို့ ကူးယူပြီးပါပြီ)။")
                        : UiLocalizer.Pick(
                            "⚠ Could not insert text into the active slide.",
                            "⚠ ဆလိုက်အတွင်း စာသားထည့်သွင်း၍ မရပါ။"),
                    ok);
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
                    foreach (var cached in _scannerToolkit.GetCachedIssuesForActivePresentation())
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                            cached.IsResolved = true;
                    }
                    _scannerToolkit.NotifyVisualUnderlinesInvalidated();
                }
                return true;
            };
            ctrl.RequestRemoveCustomWord += word =>
            {
                if (_proofingEngine == null || !_proofingEngine.RemoveCustomWord(word, _customDictFilePath))
                    return false;

                if (_scannerToolkit != null)
                    _scannerToolkit.RunScan(_scannerToolkit.CurrentScope);
                return true;
            };

            // Presentation Toolkit Actions
            ctrl.RequestConvertLegacyFonts += selectionOnly =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) { ctrl.SetScanningState(false); return; }
                PowerPointToolkitOutcome res;
                try { res = _scannerToolkit.ConvertLegacyFontsAndUnifyPyidaungsu(selectionOnly, convertLegacyEncoding: true); }
                finally { ctrl.SetScanningState(false); }
                ctrl.UpdateQualityReport(null);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestUnifyMyanmarFonts += selectionOnly =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) { ctrl.SetScanningState(false); return; }
                PowerPointToolkitOutcome res;
                try { res = _scannerToolkit.ConvertLegacyFontsAndUnifyPyidaungsu(selectionOnly, convertLegacyEncoding: false); }
                finally { ctrl.SetScanningState(false); }
                ctrl.UpdateQualityReport(null);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestInjectSmartSlideZwsp += selectionOnly =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) { ctrl.SetScanningState(false); return; }
                PowerPointToolkitOutcome res;
                try { res = _scannerToolkit.ApplySmartSlideLineWrapZwsp(selectionOnly, stripAllInsteadOfInject: false); }
                finally { ctrl.SetScanningState(false); }
                ctrl.UpdateQualityReport(null);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestStripAllZwsp += selectionOnly =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) { ctrl.SetScanningState(false); return; }
                PowerPointToolkitOutcome res;
                try { res = _scannerToolkit.ApplySmartSlideLineWrapZwsp(selectionOnly, stripAllInsteadOfInject: true); }
                finally { ctrl.SetScanningState(false); }
                ctrl.UpdateQualityReport(null);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestRepairBrokenPdfText += selectionOnly =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) { ctrl.SetScanningState(false); return; }
                PowerPointToolkitOutcome res;
                try { res = _scannerToolkit.RepairBrokenPdfInScope(selectionOnly); }
                finally { ctrl.SetScanningState(false); }
                ctrl.UpdateQualityReport(null);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestPasteCleanClipboard += () =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) return;
                PowerPointToolkitOutcome res = _scannerToolkit.PasteAndRepairClipboardIntoActiveSlide();
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestFormatNumerals += (toMyanmar, selectionOnly) =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) { ctrl.SetScanningState(false); return; }
                PowerPointToolkitOutcome res;
                try { res = _scannerToolkit.ApplySlideNumeralStyle(selectionOnly, toMyanmar); }
                finally { ctrl.SetScanningState(false); }
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestRewriteToOfficialStyle += selectionOnly =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) { ctrl.SetScanningState(false); return; }
                PowerPointToolkitOutcome res;
                try { res = _scannerToolkit.RewriteOfficialProseInScope(selectionOnly); }
                finally { ctrl.SetScanningState(false); }
                ctrl.UpdateQualityReport(null);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestFormatPunctuation += selectionOnly =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) { ctrl.SetScanningState(false); return; }
                PowerPointToolkitOutcome res;
                try { res = _scannerToolkit.ApplyOfficialSlideTypography(selectionOnly, convertDigitsToMyanmar: false); }
                finally { ctrl.SetScanningState(false); }
                ctrl.UpdateQualityReport(null);
                ctrl.ShowToastBanner(res.UserMessage, res.Success);
            };
            ctrl.RequestRefreshQualityReport += () =>
            {
                EnsureEngineAttachedOnUiThread();
                return _scannerToolkit?.BuildActivePresentationQualityReport(forceRefresh: true) ?? new DocumentQualityReport();
            };
            ctrl.RequestExportAuditWorkbook += () =>
                ExecuteExportPowerPointProofingReport(ProofingReportExportFormat.PrintableA4WordDocument);
            ctrl.RequestExportProofingReport += ExecuteExportPowerPointProofingReport;
        }

        private string GetActivePresentationTitle()
        {
            try
            {
                string? name = _pptApp?.ActivePresentation?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    return name!;
            }
            catch { }
            return UiLocalizer.Pick("Untitled Presentation", "အမည်မပေးရသေးသော PowerPoint ဆလိုက်တွဲ");
        }

        private void RecordPresentationSessionFix(PresentationSlideIssue item, string appliedReplacement)
        {
            if (item == null || string.IsNullOrEmpty(appliedReplacement))
                return;

            string presTitle = GetActivePresentationTitle();
            if (!_sessionFixedByPresentation.TryGetValue(presTitle, out var list))
            {
                list = new List<ProofingSessionFixedRecord>();
                _sessionFixedByPresentation[presTitle] = list;
            }

            if (list.Count >= 2000)
                list.RemoveAt(0);

            list.Add(new ProofingSessionFixedRecord(
                item.DisplayLocation,
                item.Issue.OriginalText ?? string.Empty,
                appliedReplacement,
                DateTime.Now));
        }

        private ProofingAuditReportModel BuildActivePresentationProofingAuditReportModel()
        {
            string presTitle = GetActivePresentationTitle();
            var orderedSlides = _scannerToolkit?.GetAllSlideDisplayNamesForActivePresentation() ?? new List<string>();
            var allDeckIssues = _scannerToolkit != null
                ? _scannerToolkit.EnsureEntirePresentationScannedForAuditReport()
                : Array.Empty<PresentationSlideIssue>();
            var unresolved = allDeckIssues.Where(i => !i.IsResolved).ToList();
            var qReport = _scannerToolkit?.BuildActivePresentationQualityReport(forceRefresh: true) ?? new DocumentQualityReport();

            bool en = UiLocalizer.IsEnglish;
            var auditInputs = unresolved
                .Select(item =>
                {
                    string slideLabel = item.ContainerKind == PresentationContainerKind.SlideMasterShape
                        ? (string.IsNullOrWhiteSpace(item.Target.MasterOrLayoutName)
                            ? (en ? "Master" : "မူရင်းပုံစံ")
                            : item.Target.MasterOrLayoutName)
                        : (en
                            ? $"Slide {item.SlideNumber}"
                            : $"ဆလိုက် {item.SlideNumber}");
                    return new ProofingAuditItemInput(
                        item.DisplayLocation,
                        item.SnapshotText,
                        item.Issue,
                        (item.Issue.Suggestions != null && item.Issue.Suggestions.Count > 0)
                            ? item.Issue.Suggestions[0].Replacement
                            : string.Empty,
                        pageOrSheetLabel: slideLabel,
                        paragraphOrCellLabel: item.Target.ShapeName);
                })
                .ToList();

            IReadOnlyList<ProofingSessionFixedRecord> sessionFixed =
                _sessionFixedByPresentation.TryGetValue(presTitle, out var list)
                    ? list.ToList()
                    : Array.Empty<ProofingSessionFixedRecord>();

            return ProofingAuditReportBuilder.Build(
                presTitle,
                "Microsoft PowerPoint",
                _proofingEngine?.StyleMode ?? _pendingStyleMode,
                qReport,
                auditInputs,
                sessionFixed,
                allOrderedSectionNames: orderedSlides.Count > 0 ? orderedSlides : null);
        }

        private void ExecuteExportPowerPointProofingReport(ProofingReportExportFormat format)
        {
            if (_scannerToolkit == null)
                return;

            ProofingAuditReportModel model = BuildActivePresentationProofingAuditReportModel();
            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.SetScanScope(_scannerToolkit.CurrentScope);

            switch (format)
            {
                case ProofingReportExportFormat.PrintableA4WordDocument:
                case ProofingReportExportFormat.NativeOfficeDocument:
                {
                    try
                    {
                        string docxPath = ProofingAuditWordDocumentExporter.SavePrintableA4DocxToDisk(model);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(docxPath)
                        {
                            UseShellExecute = true
                        });
                        ctrl?.ShowToastBanner(UiLocalizer.Pick(
                            $"✓ Opened Printable A4 Word Audit Report (.docx — all {Math.Max(1, model.SectionBreakdowns.Count)} slide(s) audited: {model.ConfirmedWrongCount} wrong • {model.SuspiciousReviewCount} review).",
                            $"✓ ဆလိုက်အားလုံး ({Math.Max(1, model.SectionBreakdowns.Count)} ချပ်) ပါဝင်သော ပုံနှိပ်ရန်အသင့် A4 Word အစီရင်ခံစာ (.docx) ကို ဖွင့်ပေးပြီးပါပြီ။"));
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

        private void CloseActivePowerPointReviewDialog()
        {
            var dlg = _activeReviewDialog;
            _activeReviewDialog = null;
            _activeReviewPresentationName = string.Empty;
            if (dlg != null && !dlg.IsDisposed)
            {
                try { dlg.Close(); } catch { }
                try { dlg.Dispose(); } catch { }
            }
        }

        private static Func<PresentationSlideIssue, bool> GetPowerPointFilterPredicate(IssueFilterMode filterMode) =>
            filterMode switch
            {
                IssueFilterMode.SpellingOnly => i => !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning,
                IssueFilterMode.GrammarOnly => i => i.Issue.IsGrammarOrStyle,
                IssueFilterMode.ContextOnly => i => i.Issue.IsContextualWarning,
                _ => _ => true
            };

        private static string GetPowerPointFilterScopeLabel(IssueFilterMode filterMode) =>
            filterMode switch
            {
                IssueFilterMode.SpellingOnly => UiLocalizer.Pick("Spelling Issues", "သတ်ပုံအမှား"),
                IssueFilterMode.GrammarOnly => UiLocalizer.Pick("Grammar / Style Issues", "သဒ္ဒါ/စာဟန်အမှား"),
                IssueFilterMode.ContextOnly => UiLocalizer.Pick("Context / Meaning Checks", "အနက်သံသယ စိစစ်ချက်"),
                _ => UiLocalizer.Pick("All Issues", "အမှားစုစုပေါင်း")
            };

        private List<FixPreviewEntry<PresentationSlideIssue>> BuildPowerPointFixPreviewEntries(IssueFilterMode filterMode)
        {
            if (_scannerToolkit == null)
                return new List<FixPreviewEntry<PresentationSlideIssue>>();

            var predicate = GetPowerPointFilterPredicate(filterMode);
            return _scannerToolkit.GetCachedIssuesForActivePresentation()
                .Where(i => !i.IsResolved &&
                            i.Issue.Suggestions != null &&
                            i.Issue.Suggestions.Count > 0 &&
                            predicate(i))
                .Select(item => new FixPreviewEntry<PresentationSlideIssue>(
                    item,
                    item.DisplayLocation,
                    item.SnapshotText,
                    item.Issue))
                .ToList();
        }

        private int ApplyPowerPointApprovedBatch(
            PowerPointProofingTaskPaneControl? ctrl,
            IReadOnlyDictionary<PresentationSlideIssue, string> approvedReplacements,
            string scopeLabel)
        {
            _ = scopeLabel;
            if (_scannerToolkit == null || approvedReplacements == null || approvedReplacements.Count == 0)
                return 0;

            int fixedCount;
            try
            {
                fixedCount = _scannerToolkit.ApplyBatchSelectedFixes(approvedReplacements);
            }
            finally
            {
                ctrl?.SetScanningState(false);
            }

            ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActivePresentation());

            if (fixedCount > 0)
            {
                foreach (var kvp in approvedReplacements)
                {
                    if (kvp.Key.IsResolved)
                        RecordPresentationSessionFix(kvp.Key, kvp.Value);
                }
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Applied {fixedCount} approved fix(es) while preserving slide fonts, colors & animations (↶ Undo available).",
                    $"✓ စိစစ်အတည်ပြုထားသော ({fixedCount}) ခုကို ဆလိုက်ဒီဇိုင်း မပျက်စေဘဲ ပြင်ဆင်ပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။"));
            }
            return fixedCount;
        }

        private void ExecuteFixAllInActiveScope(IssueFilterMode filterMode = IssueFilterMode.All)
        {
            if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null)
                return;

            _activeReviewFilterMode = filterMode;
            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            var previewEntries = BuildPowerPointFixPreviewEntries(filterMode);

            if (previewEntries.Count == 0)
            {
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    "✓ No remaining issues to fix in this presentation scope.",
                    "✓ ပြင်ဆင်ရန် အမှားအယွင်း မကျန်ရှိတော့ပါ။"));
                return;
            }

            var prefs = ReviewWindowPreferencesStore.Load("powerpoint");
            if (prefs.PreferDockedInTaskPane && ctrl != null)
            {
                CloseActivePowerPointReviewDialog();
                ctrl.EnterDockedChecklistMode(previewEntries);
                return;
            }

            OpenOrUpdatePowerPointModelessReviewStudio(filterMode, ctrl, previewEntries, forceFloating: true);
        }

        private void OpenOrUpdatePowerPointModelessReviewStudio(
            IssueFilterMode filterMode,
            PowerPointProofingTaskPaneControl? ctrl,
            IReadOnlyList<FixPreviewEntry<PresentationSlideIssue>> previewEntries,
            bool forceFloating)
        {
            if (_scannerToolkit == null)
                return;

            string presName = GetActivePresentationTitle();
            string scopeLabel = GetPowerPointFilterScopeLabel(filterMode);
            string scopeTitle = $"{presName} — {scopeLabel}";

            if (_activeReviewDialog != null && !_activeReviewDialog.IsDisposed)
            {
                _activeReviewPresentationName = presName;
                _activeReviewFilterMode = filterMode;
                if (_activeReviewDialog.WindowState == FormWindowState.Minimized)
                    _activeReviewDialog.WindowState = FormWindowState.Normal;
                _activeReviewDialog.ReloadEntriesFromHost(previewEntries, scopeTitle);
                _activeReviewDialog.Show();
                _activeReviewDialog.BringToFront();
                _activeReviewDialog.Activate();
                return;
            }

            var callbacks = new FixPreviewStudioCallbacks<PresentationSlideIssue>
            {
                HostTag = "powerpoint",
                OnNavigateToItem = target => _scannerToolkit.NavigateToIssue(target),
                OnApplyBatch = approved => ApplyPowerPointApprovedBatch(ctrl, approved, scopeLabel),
                OnIgnoreItems = items =>
                {
                    foreach (var item in items)
                    {
                        _proofingEngine?.IgnoreWordInSession(item.Issue.OriginalText);
                        item.IsResolved = true;
                    }
                    _scannerToolkit.NotifyVisualUnderlinesInvalidated();
                    ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActivePresentation());
                },
                OnAddWordToDictionary = (word, items) =>
                {
                    _proofingEngine?.AddCustomWord(word, _customDictFilePath);
                    string normAdded = UnicodeNormalizer.NormalizeCanonicalOrder(word.Trim());
                    foreach (var cached in _scannerToolkit.GetCachedIssuesForActivePresentation())
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                            cached.IsResolved = true;
                    }
                    _scannerToolkit.NotifyVisualUnderlinesInvalidated();
                    ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActivePresentation());
                    ctrl?.ShowToastBanner(UiLocalizer.Pick(
                        $"✓ Added \"{word}\" to your Personal Dictionary (Word + Excel + PowerPoint).",
                        $"✓ \"{word}\" ကို ကိုယ်ပိုင်အဘိဓာန် (Word + Excel + PowerPoint) သို့ ထည့်သွင်းပြီးပါပြီ။"));
                },
                OnRefreshEntries = () => BuildPowerPointFixPreviewEntries(_activeReviewFilterMode),
                OnUndoLastFix = ExecuteUndoLastAction,
                OnDockInTaskPane = snapshot =>
                {
                    var paneCtrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                    paneCtrl?.EnterDockedChecklistMode(snapshot);
                },
                OnExportReport = ExecuteExportPowerPointProofingReport
            };

            if (forceFloating)
            {
                var p = ReviewWindowPreferencesStore.Load("powerpoint");
                if (p.PreferDockedInTaskPane)
                {
                    p.PreferDockedInTaskPane = false;
                    ReviewWindowPreferencesStore.Save("powerpoint", p);
                }
            }

            var dlg = new FixAllPreviewDialog<PresentationSlideIssue>(
                previewEntries,
                scopeTitle,
                callbacks.OnNavigateToItem,
                callbacks);

            _activeReviewDialog = dlg;
            _activeReviewPresentationName = presName;
            dlg.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_activeReviewDialog, dlg))
                {
                    _activeReviewDialog = null;
                    _activeReviewPresentationName = string.Empty;
                }
                try { dlg.Dispose(); } catch { }
            };

            dlg.Show();
        }

        private void ExecuteUndoLastAction()
        {
            if (_scannerToolkit == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            bool ok = _scannerToolkit.UndoLastAction(out string restored);
            ctrl?.ShowToastBanner(
                ok
                    ? UiLocalizer.Pick($"✓ Reverted: {restored}", $"✓ မူလအတိုင်း ပြန်ဆုတ်ပြီးပါပြီ: {restored}")
                    : UiLocalizer.Pick("⚠ No further slide undo steps available.", "⚠ ပြန်ဆုတ်ရန် မှတ်တမ်း မရှိတော့ပါ။"),
                ok);
        }

        private void TriggerScanWithDbHotReload(PresentationScanScope scope)
        {
            var activeCtrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            activeCtrl?.SetScanScope(scope);

            if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true, requestedScope: scope) ||
                _proofingEngine == null ||
                _scannerToolkit == null)
            {
                return;
            }

            if (PowerPointDeckScannerAndToolkit.IsProtectedViewOnly(_pptApp))
            {
                activeCtrl?.ShowToastBanner(
                    UiLocalizer.Pick(
                        "⚠ Presentation is open in Protected View (Read-Only). Click 'Enable Editing' on PowerPoint's yellow bar at the top, then click Scan!",
                        "⚠ ဆလိုက်ဖိုင်သည် Protected View (Read-Only) ဖြင့် ပွင့်နေပါသည်။ PowerPoint အပေါ်ဘက်ရှိ 'Enable Editing' ကို အရင်နှိပ်ပြီးမှ Scan ပြန်နှိပ်ပါ။"),
                    isSuccess: false);
                return;
            }

            if (!PowerPointDeckScannerAndToolkit.IsPowerPointReadyForInterop(_pptApp))
            {
                activeCtrl?.ShowToastBanner(
                    UiLocalizer.Pick(
                        "⚠ Please open or create a PowerPoint presentation slide first (and exit Slide Show if active), then click Scan.",
                        "⚠ ကျေးဇူးပြု၍ PowerPoint ဆလိုက်တစ်ခု အရင်ဖွင့်ပြီးမှ Scan နှိပ်ပါ။"),
                    isSuccess: false);
                return;
            }

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
                            _scannerToolkit.RunScan(scope);
                        });
                    }
                    catch
                    {
                        SafeBeginInvokeUi(_uiMarshaller, () => _scannerToolkit?.RunScan(scope));
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _dbHotReloadInProgress, 0);
                    }
                });
                return;
            }

            _scannerToolkit.RunScan(scope);
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

        private static bool LoadSavedBoolSetting(string path, bool defaultVal)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string raw = File.ReadAllText(path).Trim();
                    if (bool.TryParse(raw, out bool parsed))
                        return parsed;
                }
            }
            catch { }
            return defaultVal;
        }

        private void ApplySingleIssueFixFromUi(PresentationSlideIssue item, string replacement)
        {
            if (_scannerToolkit == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
            if (_scannerToolkit.ApplySingleSuggestion(item, replacement))
            {
                RecordPresentationSessionFix(item, replacement);
                ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActivePresentation());
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Applied \"{replacement}\" in {item.DisplayLocation} (↶ Undo available).",
                    $"✓ {item.DisplayLocation} တွင် \"{replacement}\" ဖြင့် ပြင်ဆင်ပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။"));
            }
            else
            {
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    "⚠ Could not apply replacement (slide text may have changed — click Scan).",
                    "⚠ ပြင်ဆင်၍ မရပါ (ဆလိုက်စာသား ပြောင်းလဲသွားနိုင်သဖြင့် Scan ပြန်နှိပ်ပါ)။"), isSuccess: false);
            }
        }

        private void ApplySameWordBatchFixFromUi(string orig, string rep)
        {
            if (_scannerToolkit == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
            var preMatching = _scannerToolkit.GetCachedIssuesForActivePresentation()
                .Where(i => !i.IsResolved && string.Equals(i.Issue.OriginalText, orig, StringComparison.Ordinal))
                .ToList();

            int count = _scannerToolkit.ApplyBatchSuggestionAcrossPresentation(orig ?? string.Empty, rep ?? string.Empty);
            if (count > 0)
            {
                foreach (var matched in preMatching)
                {
                    if (matched.IsResolved)
                        RecordPresentationSessionFix(matched, rep ?? string.Empty);
                }
            }

            ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActivePresentation());
            if (count > 0)
            {
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Updated {count} matching occurrence(s) across slides with \"{rep}\" (↶ Undo available).",
                    $"✓ ဆလိုက်များရှိ တူညီသောအမှား ({count}) နေရာလုံးကို \"{rep}\" ဖြင့် ပြင်ဆင်ပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။"));
            }
        }

        private void IgnoreIssueFromUi(PresentationSlideIssue item)
        {
            _proofingEngine?.IgnoreWordInSession(item.Issue.OriginalText);
            item.IsResolved = true;
            if (_scannerToolkit != null)
            {
                _scannerToolkit.NotifyVisualUnderlinesInvalidated();
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActivePresentation());
            }
        }

        private void AddIssueWordToDictionaryFromUi(PresentationSlideIssue item)
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
                foreach (var cached in _scannerToolkit.GetCachedIssuesForActivePresentation())
                {
                    if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                    {
                        cached.IsResolved = true;
                    }
                }
                _scannerToolkit.NotifyVisualUnderlinesInvalidated();
                ctrl?.RenderIssues(_scannerToolkit.GetCachedIssuesForActivePresentation());
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    $"✓ Added \"{item.Issue.OriginalText}\" to your Personal Dictionary (Word + Excel + PowerPoint).",
                    $"✓ \"{item.Issue.OriginalText}\" ကို ကိုယ်ပိုင်အဘိဓာန် (Word + Excel + PowerPoint) သို့ မှတ်သားပြီးပါပြီ။"));
            }
        }

        private void ExecuteClearPowerPointUnderlines()
        {
            if (_scannerToolkit == null)
                return;

            _quickFixController?.CloseActivePopup();
            _wavyOverlay?.HideOverlayImmediate();

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            int cleaned = _scannerToolkit.ClearAllVisualUnderlinesInActivePresentation();
            ctrl?.ShowToastBanner(
                cleaned > 0
                    ? UiLocalizer.Pick(
                        $"✓ Hid screen-only wavy underlines for {cleaned} issue(s) (slide design & .pptx file untouched).",
                        $"✓ တွေ့ရှိချက် ({cleaned}) ခု၏ ဖန်သားပြင်ပေါ်ရှိ မျဉ်းလှိုင်းများကို ဖျောက်ပြီးပါပြီ (.pptx ဖိုင် လုံးဝမပြောင်းလဲပါ)။")
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
            _scannerToolkit.RunScan(_scannerToolkit.CurrentScope);
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

        // ================= POWERPOINT RIBBON UI =================

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
                        imageMso='SpellingAndGrammar'
                        getPressed='GetTaskPanePressed'
                        onAction='OnRibbonToggleTaskPane'
                        getScreentip='GetRibbonScreentip' />
          <button id='BtnReviewScanNow'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='AcceptInvitation'
                  onAction='OnRibbonScanActiveSlide'
                  getScreentip='GetRibbonScreentip' />
        </group>
      </tab>
      <tab id='TabMyanmarPowerPointProofing' getLabel='GetRibbonLabel'>
        <group id='GrpPptScanAndFix' getLabel='GetRibbonLabel'>
          <button id='BtnScanActiveSlide'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='SpellingAndGrammar'
                  onAction='OnRibbonScanActiveSlide'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnScanSelection'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='ObjectsSelect'
                  onAction='OnRibbonScanSelection'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnScanPresentation'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='SlideSorterView'
                  onAction='OnRibbonScanPresentation'
                  getScreentip='GetRibbonScreentip' />
          <toggleButton id='BtnToggleIncludeMasters'
                        getLabel='GetRibbonLabel'
                        size='normal'
                        imageMso='SlideMasterView'
                        getPressed='GetIncludeMastersPressed'
                        onAction='OnRibbonToggleIncludeMasters'
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
        <group id='GrpPptStyleAndLive' getLabel='GetRibbonLabel'>
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
          <button id='BtnClearPptUnderlines'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='ClearFormatting'
                  onAction='OnRibbonClearUnderlines'
                  getScreentip='GetRibbonScreentip' />
        </group>
        <group id='GrpPptDictionary' getLabel='GetRibbonLabel'>
          <button id='BtnSearchDictionary'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='LookUpReference'
                  onAction='OnRibbonSearchDictionary'
                  getScreentip='GetRibbonScreentip' />
          <toggleButton id='BtnToggleTaskPane'
                        getLabel='GetRibbonLabel'
                        size='large'
                        imageMso='ResearchPane'
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
        <group id='GrpPptTypography' getLabel='GetRibbonLabel'>
          <button id='BtnConvertFonts'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='FontDialog'
                  onAction='OnRibbonConvertFonts'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnUnifyMyanmarFonts'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='FontSchemes'
                  onAction='OnRibbonUnifyMyanmarFonts'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnSmartSlideZwsp'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='HyphenationDialog'
                  onAction='OnRibbonSmartSlideZwsp'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnRepairBrokenPdf'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='FileSaveAsPdfOrXps'
                  onAction='OnRibbonRepairBrokenPdf'
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
        <group id='GrpPptQuality' getLabel='GetRibbonLabel'>
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
        }

        public string GetRibbonLabel(Office.IRibbonControl control)
        {
            ProofingStyleMode activeMode = _proofingEngine?.StyleMode ?? _pendingStyleMode;
            return control.Id switch
            {
                "TabMyanmarPowerPointProofing" => UiLocalizer.Pick("Myanmar Spell Checker", "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး"),
                "GrpReviewMyanmarSpellChecker" => UiLocalizer.Pick("Myanmar Spell Checker", "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး"),
                "BtnReviewTogglePane" => UiLocalizer.Pick("Myanmar Spell Checker", "မြန်မာစာ စာစစ်ပြကွက်"),
                "BtnReviewScanNow" => UiLocalizer.Pick("Scan Slide", "Slide စစ်ဆေးမည်"),
                "GrpPptScanAndFix" => UiLocalizer.Pick("Slide & Notes Proofing (Zero-Mutation)", "ဆလိုက်နှင့် မှတ်စု စစ်ဆေးရေး"),
                "BtnScanActiveSlide" => UiLocalizer.Pick("Scan Active Slide", "လက်ရှိ Slide စစ်မည်"),
                "BtnScanSelection" => UiLocalizer.Pick("Scan Selection", "ရွေးထားသည့်ပုံ စစ်မည်"),
                "BtnScanPresentation" => UiLocalizer.Pick("Scan All Slides", "Slide အားလုံး စစ်မည်"),
                "BtnToggleIncludeMasters" => UiLocalizer.Pick("Include Masters", "Slide Master ပါစစ်မည်"),
                "BtnFixAllIssues" => UiLocalizer.Pick("Review & Fix All", "အားလုံး တစ်ပြိုင်နက် ပြင်မည်"),
                "BtnUndoLastFix" => UiLocalizer.Pick("Undo Last Fix", "ပြန်ဆုတ်မည် (Undo)"),

                "GrpPptStyleAndLive" => UiLocalizer.Pick("Style & Live Options", "အရေးအသားနှင့် အလိုအလျောက်စနစ်"),
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
                "BtnToggleLiveScan" => UiLocalizer.Pick("Live Slide Scan", "အလိုအလျောက် စစ်ဆေးမှု (Live)"),
                "BtnToggleVisualUnderlines" => UiLocalizer.Pick("Show Underlines", "အနီမျဉ်း ပြသမှု (Underlines)"),
                "BtnClearPptUnderlines" => UiLocalizer.Pick("Clear Marks", "မျဉ်းရှင်းမည်"),

                "GrpPptDictionary" => UiLocalizer.Pick("Dictionary & Workspace (Offline)", "အဘိဓာန်နှင့် စစ်ဆေးရေးပြကွက် (Offline)"),
                "BtnSearchDictionary" => UiLocalizer.Pick("Dictionary & Encyclopedia", "သတ်ပုံကျမ်းနှင့် အဘိဓာန် ရှာရန်"),
                "BtnToggleTaskPane" => UiLocalizer.Pick("Show / Hide Task Pane", "စစ်ဆေးရေးပြကွက် ပြ/ဖျောက်"),
                "BtnManageCustomWords" => UiLocalizer.Pick("Custom Words", "မှတ်သားဝေါဟာရ စီမံရန်"),
                "BtnToggleUiLanguage" => UiLocalizer.Pick("UI: English ➔ မြန်မာ", "UI: မြန်မာ ➔ English"),
                "BtnLicenseActivation" => UiLocalizer.Pick("About & License (GPLv3)", "ဖန်တီးသူနှင့် လိုင်စင် (GPLv3)"),
                "MnuUserGuide" => UiLocalizer.Pick("User Guide", "အသုံးပြုပုံ လမ်းညွှန်"),
                "BtnGuideHtml" => UiLocalizer.Pick("Interactive Searchable Guide (HTML)", "အပြန်အလှန် ရှာဖွေဖတ်ရှုရန် လမ်းညွှန် (HTML)"),
                "BtnGuideDocx" => UiLocalizer.Pick("Printable A4 Manual (Word .docx)", "ပုံနှိပ်ရန်အသင့် A4 လမ်းညွှန်စာအုပ် (.docx)"),

                "GrpPptTypography" => UiLocalizer.Pick("Slide Font, ZWSP & Numerals", "ဖောင့်၊ ZWSP နှင့် ဂဏန်း ကိရိယာ"),
                "BtnConvertFonts" => "WinInnwa/Zawgyi ➔ Pyidaungsu",
                "BtnUnifyMyanmarFonts" => UiLocalizer.Pick("Unify Myanmar Font", "မြန်မာဖောင့် Pyidaungsu ညှိမည်"),
                "BtnSmartSlideZwsp" => UiLocalizer.Pick("Smart Slide Line-Wrap", "ဆလိုက် စာကြောင်းဆင်း ZWSP"),
                "BtnRepairBrokenPdf" => UiLocalizer.Pick("Repair Garbled PDF", "PDF စာသားပျက်ပြင်ရန်"),
                "BtnNumeralsToMyanmar" => UiLocalizer.Pick("0-9 ➔ ၀-၉ (Slides)", "0-9 ➔ ၀-၉ (ဆလိုက်)"),
                "BtnCleanPunctuation" => UiLocalizer.Pick("Clean ၊ / ။ Spacing", "ပုဒ်ဖြတ် ၊ / ။ ညှိမည်"),

                "GrpPptQuality" => UiLocalizer.Pick("Presentation Toolkit", "ဆလိုက် အရည်အသွေးနှင့် ကိရိယာ"),
                "BtnOpenToolkitPane" => UiLocalizer.Pick("Toolkit & Calendar", "ကိရိယာနှင့် ပြက္ခဒိန်"),
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
                    "Scan TextBoxes, Tables, Grouped Shapes & Speaker Notes on the active PowerPoint slide",
                    "လက်ရှိဆလိုက်ရှိ TextBox၊ ဇယား၊ Group ပုံ နှင့် Speaker Notes များကို စစ်ဆေးရန်"),
                "BtnScanActiveSlide" => UiLocalizer.Pick(
                    "Check Slide TextBoxes, Tables, Grouped Shapes & Speaker Notes on the active slide with zero design mutation",
                    "လက်ရှိဆလိုက်ရှိ TextBox၊ ဇယား၊ Group ပုံ နှင့် Speaker Notes များကို ဒီဇိုင်းမပျက်စေဘဲ စစ်ဆေးရန်"),
                "BtnScanSelection" => UiLocalizer.Pick(
                    "Scan only the currently selected shape(s), table, or slides",
                    "ရွေးချယ်ထားသော ပုံ သို့မဟုတ် ဆလိုက်များကိုသာ စစ်ဆေးရန်"),
                "BtnScanPresentation" => UiLocalizer.Pick(
                    "Scan every slide, table, grouped shape, and speaker note across the entire presentation",
                    "PowerPoint ဆလိုက်တွဲ တစ်ခုလုံးရှိ ဆလိုက်နှင့် မှတ်စုအားလုံးကို စစ်ဆေးရန်"),
                "BtnToggleIncludeMasters" => UiLocalizer.Pick(
                    "Include Slide Masters & Custom Layouts when scanning All Slides",
                    "Slide အားလုံး စစ်ဆေးရာတွင် Slide Master နှင့် Layout ပုံစံခွက်များပါ ထည့်သွင်းစစ်ဆေးရန်"),
                "BtnFixAllIssues" => UiLocalizer.Pick(
                    "Open the Review & Fix All Studio to batch-apply replacements while preserving bold, size, RGB color & animations",
                    "ဆလိုက်ဒီဇိုင်း၊ အရောင် နှင့် Animation မပျက်စေဘဲ အမှားအားလုံးကို စိစစ်ပြင်ဆင်ရန်"),
                "BtnUndoLastFix" => UiLocalizer.Pick(
                    "Restore slide containers modified by the most recent proofing or toolkit action",
                    "နောက်ဆုံး ပြင်ဆင်ခဲ့သော ဆလိုက်စာသားများကို မူလအတိုင်း ပြန်ဆုတ်ရန်"),
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
                    "Automatically check the active slide (450 ms idle debounce; auto-paused during Slide Show F5)",
                    "လက်ရှိဆလိုက်ကို အလိုအလျောက် စစ်ဆေးပေးရန် (Slide Show F5 ပြနေစဉ် အလိုအလျောက် ရပ်နားထားသည်)"),
                "BtnToggleVisualUnderlines" => UiLocalizer.Pick(
                    "Show/hide screen-only red, blue & amber wavy underlines on slides (.pptx files are never modified)",
                    "ဖန်သားပြင်ပေါ်တွင် အနီ/အပြာ/လိမ္မော် မျဉ်းလှိုင်း ပြသမှုကို ဖွင့်/ပိတ်ရန် (.pptx ဖိုင်ကို လုံးဝ မပြောင်းလဲပါ)"),
                "BtnClearPptUnderlines" => UiLocalizer.Pick(
                    "Immediately hide all screen-only proofing wavy underlines in the active presentation",
                    "ဖန်သားပြင်ပေါ်ရှိ စစ်ဆေးရေး မျဉ်းလှိုင်းအားလုံးကို ချက်ချင်းဖျောက်ရန်"),
                "BtnSearchDictionary" => UiLocalizer.Pick(
                    "Search the 100% offline Spelling Lexicon, Myanmar Dictionary & Encyclopedia (25,000+ entries)",
                    "သတ်ပုံကျမ်း၊ မြန်မာ-မြန်မာ အဘိဓာန် နှင့် စွယ်စုံဗဟုသုတ (၂၅,၀၀၀+) ရှာဖွေရန်"),
                "BtnToggleTaskPane" => UiLocalizer.Pick(
                    "Show or hide the Myanmar Spell Checker task pane on the right",
                    "ညာဘက် စစ်ဆေးရေးပြကွက် (Task Pane) ကို ဖွင့်/ပိတ် ပြုလုပ်ရန်"),
                "BtnManageCustomWords" => UiLocalizer.Pick(
                    "Manage personal Myanmar words shared across Word, Excel, and PowerPoint",
                    "Word၊ Excel နှင့် PowerPoint သုံးမျိုးလုံး မျှဝေသုံးစွဲသော ကိုယ်ပိုင်ဝေါဟာရများ စီမံရန်"),
                "BtnToggleUiLanguage" => UiLocalizer.FormatLanguageTooltip(),
                "BtnLicenseActivation" => UiLocalizer.Pick(
                    "View creator credits (Htet Yar Zar, Kyaukse University) and GNU GPL v3.0 open-source license",
                    "စနစ်အချက်အလက်၊ ဖန်တီးသူ ထက်ရာဇာ (ကျောက်ဆည်တက္ကသိုလ်) နှင့် GNU GPL v3.0 Open-Source လိုင်စင် ကြည့်ရှုရန်"),
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
                    "Convert legacy WinInnwa / Zawgyi slide runs to Pyidaungsu Unicode while keeping English brand fonts & colors intact",
                    "အင်္ဂလိပ်ဖောင့်နှင့် အရောင်များ မပျက်စေဘဲ WinInnwa / ဇော်ဂျီမှ Pyidaungsu Unicode သို့ ပြောင်းလဲရန်"),
                "BtnUnifyMyanmarFonts" => UiLocalizer.Pick(
                    "Standardize all Myanmar script runs across the presentation to Pyidaungsu without changing English fonts, sizes, or colors",
                    "ဆလိုက်တစ်ခုလုံးရှိ မြန်မာစာသားများကိုသာ Pyidaungsu ဖောင့်သို့ တစ်ပြေးညီ ညှိနှိုင်းရန်"),
                "BtnSmartSlideZwsp" => UiLocalizer.Pick(
                    "Insert invisible word-boundary ZWSP so Burmese slide titles & bullets wrap cleanly between words instead of mid-syllable",
                    "ဆလိုက်ခေါင်းစဉ်နှင့် Bullet များတွင် စာလုံးအလယ်မှ မပြတ်ကျစေရန် Smart-ZWSP ထည့်သွင်းရန်"),
                "BtnRepairBrokenPdf" => UiLocalizer.Pick(
                    "Reconstruct garbled Burmese PDF copy-paste across slides into clean Pyidaungsu Unicode",
                    "ဆလိုက်များအတွင်းရှိ PDF စာသားပျက်များကို Pyidaungsu ယူနီကုဒ်အမှန်သို့ ပြန်လည်တည်ဆောက်ရန်"),
                "BtnNumeralsToMyanmar" => UiLocalizer.Pick(
                    "Convert ASCII digits (0-9) inside Myanmar slide text into Myanmar digits (၀-၉)",
                    "ဆလိုက်စာသားအတွင်းရှိ 0-9 ဂဏန်းများကို မြန်မာဂဏန်း ၀-၉ သို့ ပြောင်းလဲရန်"),
                "BtnCleanPunctuation" => UiLocalizer.Pick(
                    "Standardize Myanmar punctuation spacing (၊ / ။) across slides",
                    "ဆလိုက်များရှိ ပုဒ်ထီး (၊) နှင့် ပုဒ်မ (။) အကွာအဝေး စံညှိရန်"),
                "BtnOpenToolkitPane" => UiLocalizer.Pick(
                    "Open the Presentation Toolkit, Smart-ZWSP Studio, Kyat Lakh converter & Myanmar Calendar",
                    "ဆလိုက်ကိရိယာများ၊ Smart-ZWSP၊ ကျပ်သိန်းပြောင်းစက် နှင့် မြန်မာသက္ကရာဇ် ပြက္ခဒိန် ဖွင့်ရန်"),
                "BtnRewriteOfficialProse" => UiLocalizer.Pick(
                    "Convert colloquial Burmese phrases on slides into formal official prose",
                    "ဆလိုက်ပေါ်ရှိ စကားပြောဟန် အသုံးအနှုန်းများကို ရုံးသုံးစာပေဟန်သို့ ပြောင်းလဲရန်"),
                "BtnExportAuditReport" => UiLocalizer.Pick(
                    "Export a Friendly Excel (.xlsx) Audit Report for the active presentation",
                    "PowerPoint မြန်မာစာ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (.xlsx) ထုတ်ယူရန်"),
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

        public bool GetIncludeMastersPressed(Office.IRibbonControl control)
        {
            return _scannerToolkit?.IncludeSlideMasters ?? _pendingIncludeMasters;
        }

        public void OnRibbonToggleIncludeMasters(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleIncludeMasters", () => SetIncludeSlideMastersFromUi(pressed));
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
                    _scannerToolkit.NotifyVisualUnderlinesInvalidated();
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
                            "✓ Enabled screen-only red/blue/amber wavy underlines (slide design & .pptx files are never modified).",
                            "✓ ဖန်သားပြင်ပေါ်ရှိ အနီ/အပြာ/လိမ္မော် မျဉ်းလှိုင်း ပြသမှုကို ဖွင့်လိုက်ပါပြီ (.pptx ဖိုင် လုံးဝမပြောင်းလဲပါ)။")
                        : UiLocalizer.Pick(
                            "✓ Disabled screen-only proofing wavy underlines.",
                            "✓ ဖန်သားပြင်ပေါ်ရှိ စစ်ဆေးရေး မျဉ်းလှိုင်းပြသမှုကို ပိတ်လိုက်ပါပြီ။"));
            });
        }

        public void OnRibbonClearUnderlines(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonClearUnderlines", ExecuteClearPowerPointUnderlines);
        }

        public bool GetTaskPanePressed(Office.IRibbonControl control)
        {
            if (_pptApp == null)
                return false;

            PowerPoint.DocumentWindow? win = null;
            try { win = _pptApp.ActiveWindow; } catch { }
            int hwnd = ResolveWindowPaneSlotId(win);
            if (hwnd != 0 && _windowTaskPanes.TryGetValue(hwnd, out var entry))
                return entry.Pane.Visible;

            return _windowTaskPanes.TryGetValue(FallbackPaneSlotId, out var fallbackEntry) && fallbackEntry.Pane.Visible;
        }

        public void OnRibbonScanActiveSlide(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonScanActiveSlide", () => TriggerScanWithDbHotReload(PresentationScanScope.ActiveSlide));
        }

        public void OnRibbonScanSelection(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonScanSelection", () => TriggerScanWithDbHotReload(PresentationScanScope.Selection));
        }

        public void OnRibbonScanPresentation(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonScanPresentation", () => TriggerScanWithDbHotReload(PresentationScanScope.EntirePresentation));
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
                string sel = _scannerToolkit?.TryGetSelectedSlideText(40) ?? string.Empty;
                ctrl?.OpenDictionaryWithQuery(sel);
            });
        }

        public void OnRibbonToggleTaskPane(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleTaskPane", () =>
            {
                if (_pptApp == null) return;
                EnsureEngineAttachedOnUiThread();
                PowerPoint.DocumentWindow? win = null;
                try { win = _pptApp.ActiveWindow; } catch { }
                int hwnd = ResolveWindowPaneSlotId(win);

                if (hwnd != 0 && _windowTaskPanes.TryGetValue(hwnd, out var entry))
                {
                    entry.Pane.Visible = pressed;
                    if (pressed)
                    {
                        ScheduleStartupPaneWidthEnforcement(hwnd, entry.Pane, entry.Control, forceIfNarrow: true);
                    }
                }
                else if (_windowTaskPanes.TryGetValue(FallbackPaneSlotId, out var fbEntry))
                {
                    fbEntry.Pane.Visible = pressed;
                    if (pressed)
                    {
                        ScheduleStartupPaneWidthEnforcement(FallbackPaneSlotId, fbEntry.Pane, fbEntry.Control, forceIfNarrow: true);
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
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.ConvertLegacyFontsAndUnifyPyidaungsu(selectionOnly: false, convertLegacyEncoding: true);
                ctrl?.UpdateQualityReport(null);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonUnifyMyanmarFonts(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonUnifyMyanmarFonts", () =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.ConvertLegacyFontsAndUnifyPyidaungsu(selectionOnly: false, convertLegacyEncoding: false);
                ctrl?.UpdateQualityReport(null);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonSmartSlideZwsp(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonSmartSlideZwsp", () =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.ApplySmartSlideLineWrapZwsp(selectionOnly: false, stripAllInsteadOfInject: false);
                ctrl?.UpdateQualityReport(null);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonRepairBrokenPdf(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonRepairBrokenPdf", () =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.RepairBrokenPdfInScope(selectionOnly: false);
                ctrl?.UpdateQualityReport(null);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonNumeralsToMyanmar(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonNumeralsToMyanmar", () =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.ApplySlideNumeralStyle(selectionOnly: false, toMyanmarDigits: true);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonCleanPunctuation(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonCleanPunctuation", () =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.ApplyOfficialSlideTypography(selectionOnly: false, convertDigitsToMyanmar: false);
                ctrl?.UpdateQualityReport(null);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonOpenToolkitPane(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonOpenToolkitPane", () =>
            {
                EnsureEngineAttachedOnUiThread();
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                ctrl?.OpenToolkitTab();
            });
        }

        public void OnRibbonRewriteOfficialProse(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonRewriteOfficialProse", () =>
            {
                if (!EnsureEngineAttachedOnUiThread(showFeedbackIfNotReady: true) || _scannerToolkit == null) return;
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                var res = _scannerToolkit.RewriteOfficialProseInScope(selectionOnly: false);
                ctrl?.UpdateQualityReport(null);
                ctrl?.ShowToastBanner(res.UserMessage, res.Success);
            });
        }

        public void OnRibbonExportAuditReport(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonExportAuditReport", () =>
            {
                ExecuteExportPowerPointProofingReport(ProofingReportExportFormat.PrintableA4WordDocument);
            });
        }

        private LicenseActivationForm? _activeAboutDialog;

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
            var dlg = new LicenseActivationForm(resolvedPubKey, _licenseFilePath, totalEntries)
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

                _uiEngineAttachTimer?.Stop();
                _uiEngineAttachTimer?.Dispose();
                _uiEngineAttachTimer = null;

                foreach (int h in _paneWidthTimers.Keys.ToList())
                {
                    StopAndRemovePaneWidthTimer(h);
                }
                _settledPaneWidthHwnds.Clear();

                if (_pptApp != null)
                {
                    _pptApp.WindowSelectionChange -= OnWindowSelectionChange;
                    _pptApp.WindowActivate -= OnWindowActivate;
                    _pptApp.WindowDeactivate -= OnWindowDeactivate;
                    _pptApp.PresentationOpen -= OnPresentationOpen;
                    _pptApp.AfterNewPresentation -= OnAfterNewPresentation;
                    _pptApp.PresentationClose -= OnPresentationClose;
                    _pptApp.SlideShowBegin -= OnSlideShowBegin;
                    _pptApp.SlideShowEnd -= OnSlideShowEnd;
                }

                CloseActivePowerPointReviewDialog();
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
        public void OnStartupComplete(ref Array custom)
        {
            AddInDiagnostics.SafeExecute("OnStartupComplete", () => EnsureEngineAttachedOnUiThread());
        }
        public void OnBeginShutdown(ref Array custom) { }
    }
}
