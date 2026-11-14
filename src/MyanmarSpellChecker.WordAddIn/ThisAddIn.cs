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
using MyanmarSpellChecker.WordAddIn.Interop;
using MyanmarSpellChecker.WordAddIn.UI;
using Office = Microsoft.Office.Core;
using Word = Microsoft.Office.Interop.Word;

namespace MyanmarSpellChecker.WordAddIn
{
    /// <summary>
    /// Enterprise VSTO / COM Add-in Entry Point for Microsoft Word (Desktop Windows).
    /// Supports Multi-Document Window Synchronization, StoryRanges (Body + Tables + Headers/Footers),
    /// Live Word.Range Tracking, Original Formatting Preservation, and Instant သတ်ပုံကျမ်း Dictionary Lookup.
    /// </summary>
    [ComVisible(true)]
    [Guid("8F5A9E42-6D11-4F3B-9C80-7B12E5D94A11")]
    [ProgId("MyanmarSpellChecker.WordAddIn")]
    public class ThisAddIn : IDTExtensibility2, Office.IRibbonExtensibility, Office.ICustomTaskPaneConsumer
    {
        public const string DefaultSqlCipherKey = "MM-GOV-THATPON-SQLCIPHER-2026!";

        public const string DefaultVendorPublicKeyXml =
            "<RSAKeyValue><Modulus>w6uP9e7V5r2X1a9K0l8m3N4b5V6c7X8z9A0s1D2f3G4h5J6k7L8p9O0i1U2y3T4r5E6w7Q8a9S0d1F2g3H4j5K6l7Z8x9C0v1B2n3M4q5W6e7R8t9Y0u1I2o3P4a5S6d7F8g9H0j1K2l3Z4x5C6v7B8n9M0q1W2e3R4t5Y6u7I8o9P0a1S2d3F4g5H6j7K8l9Z0x1C2v3B4n5M6q7W8e9R0t1Y2u3I4o5P6a7S8d9F0g1H2j3K4l5Z6x7C8v9B0n1M2q3W4e5R6t7Y8u9I0o1P2a3S4d5F6g7H8j9K0l1Z2x3C4v5B6n7M8q9W0e1R2t3Y4u5I6o7P8a9S0d1F2g3H4j5K6l7Z8==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private static readonly object s_instanceGate = new object();
        private static bool s_primaryInstanceActive;

        private bool _isPrimaryInstance;
        private Word.Application? _wordApp;
        private Office.IRibbonUI? _ribbonUi;
        private Office.ICTPFactory? _ctpFactory;
        private Control? _uiMarshaller;
        private Timer? _startupTaskPaneTimer;
        private int _startupTaskPaneRetries;
        private MyanmarProofingEngine? _proofingEngine;
        private ProofingStyleMode _pendingStyleMode = ProofingStyleMode.GeneralStandard;
        private bool _pendingLiveAutoScan = true;
        private SafeUnderlineManager? _underlineManager;
        private StaDocumentScanner? _documentScanner;
        private WordContextMenuHandler? _contextMenuHandler;
        private readonly WordDocumentToolkitService _toolkitService = new WordDocumentToolkitService();

        // Per-Window Task Pane map (keyed by Word.Window.Hwnd) for Multi-Document SDI support
        private readonly Dictionary<int, (Office.CustomTaskPane Pane, ProofingTaskPaneControl Control)> _windowTaskPanes =
            new Dictionary<int, (Office.CustomTaskPane Pane, ProofingTaskPaneControl Control)>();
        private readonly HashSet<int> _settledPaneWidthHwnds = new HashSet<int>();
        private readonly Dictionary<int, Timer> _paneWidthTimers = new Dictionary<int, Timer>();

        private string _appDataFolder = string.Empty;
        private string _uiLanguageConfigPath = string.Empty;
        private string _styleModeConfigPath = string.Empty;
        private string _ecoModeConfigPath = string.Empty;
        private string _customDictFilePath = string.Empty;
        private string _licenseFilePath = string.Empty;
        private string _databaseFilePath = string.Empty;
        private string _activeDatabaseKey = string.Empty;
        private DateTime _loadedDbLastWriteUtc = DateTime.MinValue;
        private int _dbHotReloadInProgress;
        private volatile bool _bulkWriteCancelRequested;

        static ThisAddIn()
        {
            // Ensure MS Word (WINWORD.EXE) resolves dependent assemblies from the Add-in's installation directory
            // even when CLR shadow-copying or spaces/special chars exist in the user profile path.
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var asmName = new System.Reflection.AssemblyName(args.Name);
                    foreach (string dir in GetCandidateAddInDirectories(typeof(ThisAddIn).Assembly))
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
            // Guard against duplicate Office Add-in instantiation if both HKLM and HKCU Addins keys exist on a machine
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

            _wordApp = application as Word.Application;
            if (_wordApp == null)
                return;

            AddInDiagnostics.SafeExecute("OnConnection", () =>
            {
                _uiMarshaller = new Control();
                IntPtr _ = _uiMarshaller.Handle;

                InitializePathsAndStartAsyncEngineLoad();
                HookWordEvents();
            });
        }

        private void InitializePathsAndStartAsyncEngineLoad()
        {
            List<string> addInDirs = GetCandidateAddInDirectories(typeof(ThisAddIn).Assembly);
            string? installDir = addInDirs.Count > 0 ? addInDirs[0] : Path.GetDirectoryName(typeof(ThisAddIn).Assembly.Location);
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
            _customDictFilePath = Path.Combine(localAppDataDir, "custom_dictionary.txt");
            string ministryCustomDictPath = Path.Combine(programDataDir, "ministry_custom_dictionary.txt");

            // Prefer license.lic and mlc_thatpon.db in installation dir, C:\Program Files\MyanmarSpellChecker, or %ProgramData%\%LocalAppData%
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
                : Path.Combine(_appDataFolder, "mlc_thatpon.db");

            _pendingStyleMode = LoadSavedStyleMode();
            _underlineManager = new SafeUnderlineManager(_wordApp!);

            string dbPathSnapshot = _databaseFilePath;
            string localAppDbSnapshot = localAppDb;
            string licPathSnapshot = _licenseFilePath;
            string userDictSnapshot = _customDictFilePath;
            ProofingStyleMode initialMode = _pendingStyleMode;

            // Decrypt 12.4 MB database, build 89,500+ headword Trie, and pre-JIT proofing hot path off Word's UI thread!
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
            if (!_isPrimaryInstance || _wordApp == null || _underlineManager == null)
                return;

            engine.StyleMode = _pendingStyleMode;
            _proofingEngine = engine;

            _documentScanner = new StaDocumentScanner(_wordApp, _proofingEngine, _underlineManager)
            {
                IsLiveAutoScanEnabled = _pendingLiveAutoScan
            };
            _documentScanner.UserTypingDetected += () => _contextMenuHandler?.CloseActivePopup();
            _documentScanner.ScanStarted += doc =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.SetScanningState(true);
            };
            _documentScanner.ScanProgressChanged += (pct, msg) =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.UpdateScanProgress(pct, msg);
            };
            _documentScanner.EcoActiveChanged += ecoActive => SyncEcoStateAcrossTaskPanes(ecoActive);
            _documentScanner.ScanCompleted += (doc, items) =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                if (ctrl != null)
                {
                    ctrl.SetScanOutcomeMetadata(_documentScanner.GetLastScanMetadata(doc));
                    ctrl.RenderIssues(items);
                    // Use precomputed background QualityReport for full scans; invalidate lazily for incremental keystrokes!
                    ctrl.UpdateQualityReport(_documentScanner.GetCachedQualityReport(doc));
                }
                // Never auto-pop the floating Quick-Fix window during background/typing scans;
                // Quick-Fix popup opens cleanly only when the user clicks or moves caret onto an underlined word.
            };
            _documentScanner.RefreshEcoActiveStateForActiveDocument();

            _contextMenuHandler = new WordContextMenuHandler(
                _wordApp,
                _proofingEngine,
                _underlineManager,
                _documentScanner,
                () =>
                {
                    if (_wordApp?.ActiveDocument != null)
                    {
                        var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                        ctrl?.RenderIssues(_documentScanner.GetCachedIssues(_wordApp.ActiveDocument));
                    }
                },
                _customDictFilePath,
                cursorItem =>
                {
                    var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                    ctrl?.HighlightIssueFromDocumentCursor(cursorItem);
                },
                lookupWord =>
                {
                    var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                    ctrl?.OpenDictionaryWithQuery(lookupWord);
                });

            foreach (var kvp in _windowTaskPanes.Values)
            {
                kvp.Control.SetLexiconStats(
                    _proofingEngine.Trie.WordCount,
                    _proofingEngine.Trie.OfficialThatponCount,
                    _proofingEngine.Trie.RuleCount,
                    _proofingEngine.Trie.ReferenceCount,
                    _proofingEngine.Trie.EncyclopediaCount);
                kvp.Control.SetStyleMode(_proofingEngine.StyleMode);
                kvp.Control.SetEcoModeState(EcoPerformanceGovernor.CurrentPreference, _documentScanner.IsEcoActive);
                kvp.Control.RefreshActiveTabAfterEngineReady();
            }

            _ribbonUi?.Invalidate();

            // If a non-empty document is already open when background loading finishes, schedule a calm deferred scan
            // after Task Pane & Ribbon UI finish painting (huge 50+ page docs auto-scan only the nearby viewport!)
            try
            {
                Word.Document? activeDoc = _wordApp.ActiveDocument;
                if (activeDoc != null && activeDoc.Content.End > 1)
                {
                    _documentScanner.ScheduleDeferredFullDocumentScan(380);
                }
            }
            catch { }
        }

        private void HookWordEvents()
        {
            if (_wordApp == null) return;

            _wordApp.WindowSelectionChange += OnWindowSelectionChange;
            _wordApp.WindowBeforeRightClick += OnWindowBeforeRightClick;
            _wordApp.WindowActivate += OnWindowActivate;
            _wordApp.WindowDeactivate += OnWindowDeactivate;
            _wordApp.DocumentOpen += OnDocumentOpen;
            ((Word.ApplicationEvents4_Event)_wordApp).NewDocument += OnNewDocument;
            _wordApp.DocumentBeforeSave += OnDocumentBeforeSave;
            _wordApp.DocumentBeforePrint += OnDocumentBeforePrint;
            _wordApp.DocumentBeforeClose += OnDocumentBeforeClose;
        }

        private void OnWindowDeactivate(Word.Document doc, Word.Window wn)
        {
            AddInDiagnostics.SafeExecute("OnWindowDeactivate", () => _contextMenuHandler?.CloseActivePopup());
        }

        private void OnWindowSelectionChange(Word.Selection sel)
        {
            AddInDiagnostics.SafeExecute("OnWindowSelectionChange", () =>
            {
                _documentScanner?.NotifySelectionActivity(sel);
                _contextMenuHandler?.OnWindowSelectionChange(sel);
            });
        }

        private static void SuppressOfficeMissingBurmeseBannerRegistry()
        {
            // Microsoft Office has no built-in Burmese (my-MM / LCID 1109) proofing DLL and otherwise shows
            // "MISSING PROOFING TOOLS: Proofing is not available for Burmese." until the user clicks "Don't show again".
            // Pre-populate Office's "Don't show again" language list for my-MM automatically for every user.
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
                catch
                {
                    // Ignore registry access errors
                }
            }
        }

        private void OnWindowBeforeRightClick(Word.Selection sel, ref bool cancel)
        {
            bool localCancel = cancel;
            AddInDiagnostics.SafeExecute("OnWindowBeforeRightClick", () =>
            {
                _contextMenuHandler?.OnWindowBeforeRightClick(sel, ref localCancel);
            });
            cancel = localCancel;
        }

        private void OnWindowActivate(Word.Document doc, Word.Window wn)
        {
            AddInDiagnostics.SafeExecute("OnWindowActivate", () =>
            {
                PruneClosedWindowTaskPanes();

                UiLanguage savedLang = UiLocalizer.LoadSavedLanguage(_uiLanguageConfigPath);
                if (savedLang != UiLocalizer.CurrentLanguage)
                {
                    UiLocalizer.CurrentLanguage = savedLang;
                    foreach (var kvp in _windowTaskPanes.Values)
                    {
                        try { kvp.Control.ApplyLocalization(); } catch { }
                    }
                    _ribbonUi?.Invalidate();
                }

                // Synchronize Task Pane for the newly activated document window
                _documentScanner?.RefreshEcoActiveStateForActiveDocument();
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                int winHwnd = 0;
                try { winHwnd = wn?.Hwnd ?? _wordApp?.ActiveWindow?.Hwnd ?? 0; } catch { }
                if (winHwnd != 0 && _windowTaskPanes.TryGetValue(winHwnd, out var paneEntry) && !_settledPaneWidthHwnds.Contains(winHwnd))
                {
                    ScheduleStartupPaneWidthEnforcement(winHwnd, paneEntry.Pane, paneEntry.Control, forceIfNarrow: false);
                }

                if (ctrl != null && _documentScanner != null)
                {
                    ctrl.SetEcoModeState(EcoPerformanceGovernor.CurrentPreference, _documentScanner.IsEcoActive);
                    ctrl.SetScanOutcomeMetadata(_documentScanner.GetLastScanMetadata(doc));
                    ctrl.RenderIssues(_documentScanner.GetCachedIssues(doc));
                }
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
            });
        }

        private void OnDocumentOpen(Word.Document doc)
        {
            AddInDiagnostics.SafeExecute("OnDocumentOpen", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                EnforceActiveWindowStartupPaneWidth(forceIfNarrow: true);
                _documentScanner?.ScheduleDeferredFullDocumentScan(520);
            });
        }

        private void OnNewDocument(Word.Document doc)
        {
            AddInDiagnostics.SafeExecute("OnNewDocument", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                EnforceActiveWindowStartupPaneWidth(forceIfNarrow: true);
            });
        }

        private void OnDocumentBeforeSave(Word.Document doc, ref bool saveAsUI, ref bool cancel)
        {
            AddInDiagnostics.SafeExecute("OnDocumentBeforeSave", () =>
            {
                // Strip temporary Red/Blue wavy underlines (restoring original user underlines like wdUnderlineSingle)
                // before Word writes the .docx file, then automatically restore wavy underlines on unfixed issues 450ms later
                _underlineManager?.TemporarilyStripForSaveOrPrint(doc);
            });
        }

        private void OnDocumentBeforePrint(Word.Document doc, ref bool cancel)
        {
            AddInDiagnostics.SafeExecute("OnDocumentBeforePrint", () =>
            {
                // Strip temporary Red/Blue wavy underlines before printing, then restore 450ms later
                _underlineManager?.TemporarilyStripForSaveOrPrint(doc);
            });
        }

        private void OnDocumentBeforeClose(Word.Document doc, ref bool cancel)
        {
            bool willClose = !cancel;
            AddInDiagnostics.SafeExecute("OnDocumentBeforeClose", () =>
            {
                _contextMenuHandler?.CloseActivePopup();
                _documentScanner?.PurgeDocument(doc);
                _underlineManager?.PurgeClosedDocument(doc);
                PruneClosedWindowTaskPanes();
                try
                {
                    if (willClose && (_wordApp?.Documents?.Count ?? 0) <= 1)
                    {
                        _proofingEngine?.TrimMemoryCaches(
                            aggressive: EcoPerformanceGovernor.Hardware.IsUltraLowMemoryHost ||
                                        EcoPerformanceGovernor.CurrentPreference == EcoModePreference.AlwaysOn);
                    }
                }
                catch { }
            });
        }

        private void PruneClosedWindowTaskPanes()
        {
            if (_wordApp == null || _windowTaskPanes.Count == 0)
                return;

            try
            {
                var liveHwnds = new HashSet<int>();
                foreach (Word.Window win in _wordApp.Windows)
                {
                    try
                    {
                        if (win.Hwnd != 0)
                            liveHwnds.Add(win.Hwnd);
                    }
                    catch { }
                }

                var staleKeys = _windowTaskPanes.Keys.Where(h => !liveHwnds.Contains(h)).ToList();
                foreach (int staleHwnd in staleKeys)
                {
                    _windowTaskPanes.Remove(staleHwnd);
                    _settledPaneWidthHwnds.Remove(staleHwnd);
                    StopAndRemovePaneWidthTimer(staleHwnd);
                }
            }
            catch
            {
                // Ignore transient COM enumeration states
            }
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

        private bool HasOpenDocument()
        {
            try
            {
                return _wordApp != null && _wordApp.Documents != null && _wordApp.Documents.Count > 0;
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
                int hwnd = _wordApp?.ActiveWindow?.Hwnd ?? 0;
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

        private ProofingTaskPaneControl? GetOrCreateActiveWindowTaskPaneControl(bool autoShow)
        {
            if (!_isPrimaryInstance || _ctpFactory == null || _wordApp == null)
                return null;

            try
            {
                Word.Window? activeWin = null;
                try { activeWin = _wordApp.ActiveWindow; } catch { }
                int hwnd = activeWin?.Hwnd ?? 0;

                // Never create an orphan (hwnd == 0) Task Pane before ActiveWindow is ready — that causes duplicate panes once Document1 opens!
                if (activeWin == null || hwnd == 0)
                {
                    if (autoShow)
                    {
                        ScheduleDeferredTaskPaneCreation();
                    }
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
                    "MyanmarSpellChecker.ProofingTaskPaneControl",
                    UiLocalizer.Pick("Myanmar Spell Checker", "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း"),
                    activeWin);

                var ctrl = (ProofingTaskPaneControl)pane.ContentControl;
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
                ctrl.SetEcoModeState(
                    EcoPerformanceGovernor.CurrentPreference,
                    _documentScanner?.IsEcoActive ?? EcoPerformanceGovernor.Hardware.IsLowSpecHardware);
                WireTaskPaneControlEvents(ctrl);

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
                    }
                    catch { }
                };

                _windowTaskPanes[hwnd] = (pane, ctrl);
                PreSizePaneBeforeShow(hwnd, pane, ctrl);
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
                return ctrl;
            }
            catch
            {
                return null;
            }
        }

        private static void PreSizePaneBeforeShow(int winHwnd, Office.CustomTaskPane pane, ProofingTaskPaneControl ctrl)
        {
            try
            {
                IntPtr hostPtr = new IntPtr(winHwnd);
                int dpi = Math.Max(UiFontCache.GetWindowDpi(hostPtr), UiFontCache.GetEffectiveDpi(ctrl));
                float dpiScale = Math.Max(1.0f, dpi / 96.0f);
                int hostPhysicalWidth = UiFontCache.GetWindowWidthPx(hostPtr);
                int desiredWidth = (int)Math.Round(420 * dpiScale);
                int maxSafeWidth = Math.Max((int)Math.Round(320 * dpiScale), (int)(hostPhysicalWidth * 0.42));
                int targetWidth = Math.Min(desiredWidth, maxSafeWidth);
                if (pane.Width < Math.Min((int)Math.Round(360 * dpiScale), targetWidth))
                {
                    pane.Width = targetWidth;
                }
            }
            catch { }
        }

        private void ScheduleStartupPaneWidthEnforcement(
            int winHwnd,
            Office.CustomTaskPane pane,
            ProofingTaskPaneControl ctrl,
            bool forceIfNarrow)
        {
            EnsureComfortableStartupPaneWidth(winHwnd, pane, ctrl, forceIfNarrow);
            if (_settledPaneWidthHwnds.Contains(winHwnd))
            {
                StopAndRemovePaneWidthTimer(winHwnd);
                return;
            }

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

                if (_settledPaneWidthHwnds.Contains(winHwnd) || ticks >= 2)
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
            ProofingTaskPaneControl ctrl,
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

                int desiredWidth = (int)Math.Round(420 * dpiScale);
                int maxSafeWidth = Math.Max((int)Math.Round(320 * dpiScale), (int)(hostPhysicalWidth * 0.42));
                int targetWidth = Math.Min(desiredWidth, maxSafeWidth);
                int minStartupWidth = Math.Min((int)Math.Round(360 * dpiScale), targetWidth);

                if (forceIfNarrow || !_settledPaneWidthHwnds.Contains(winHwnd))
                {
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

                    // Self-calibrating guard for legacy Office hosts that interpret pane.Width in 96-DPI logical units
                    if (ctrl.IsHandleCreated && ctrl.Width > pane.Width * 1.35 && pane.Width > 520)
                    {
                        try
                        {
                            pane.Width = Math.Max(360, (int)Math.Round(targetWidth / dpiScale));
                        }
                        catch { }
                    }

                    if (!ctrl.IsDisposed)
                    {
                        ctrl.RefreshDpiMetricsAndLayout();
                    }

                    if (HasOpenDocument() &&
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
                Word.Window? win = null;
                try { win = _wordApp?.ActiveWindow; } catch { }
                if ((win != null && win.Hwnd != 0) || _startupTaskPaneRetries >= 20)
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
            foreach (var kvp in _windowTaskPanes.Values)
            {
                try { kvp.Control.SetEcoModeState(EcoPerformanceGovernor.CurrentPreference, ecoActive); } catch { }
            }
        }

        private void CycleEcoModeFromUi()
        {
            EcoModePreference next = EcoPerformanceGovernor.CyclePreference(EcoPerformanceGovernor.CurrentPreference);
            EcoPerformanceGovernor.CurrentPreference = next;
            EcoPerformanceGovernor.SavePreference(_ecoModeConfigPath, next);

            _documentScanner?.RefreshEcoActiveStateForActiveDocument();
            bool ecoActive = _documentScanner?.IsEcoActive ?? EcoPerformanceGovernor.ComputeBudgets(isLargeFile: false).IsEcoActive;
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
                    "⚡ Eco Mode set to Always On — low CPU & RAM footprint active.",
                    "⚡ ပေါ့ပါးစနစ် (Eco Mode) ကို အမြဲဖွင့်ထားပါပြီ — CPU နှင့် RAM အနည်းဆုံးဖြင့် စက်မလေးစေဘဲ စစ်ဆေးပေးပါမည်။"),
                EcoModePreference.Off => UiLocalizer.Pick(
                    "⚡ Eco Mode turned Off — full-speed scanning active.",
                    "⚡ ပေါ့ပါးစနစ်ကို ပိတ်ထားပါပြီ — စွမ်းဆောင်ရည်အပြည့်ဖြင့် စစ်ဆေးပါမည်။"),
                _ => UiLocalizer.Pick(
                    "⚡ Eco Mode set to Auto — automatically protects low-spec PCs and large documents.",
                    "⚡ ပေါ့ပါးစနစ် (Auto) အသင့်ရှိပါပြီ — စက်အားနည်းလျှင် သို့မဟုတ် စာရွက်များလျှင် အလိုအလျောက် ပေါ့ပါးစွာ စစ်ဆေးပေးပါမည်။")
            };
            ctrl?.ShowToastBanner(toast);
        }

        private void ToggleUiLanguageFromUi()
        {
            AddInDiagnostics.SafeExecute("ToggleUiLanguageFromUi", () =>
            {
                UiLanguage next = UiLocalizer.ToggleLanguage();
                UiLocalizer.SaveLanguage(_uiLanguageConfigPath, next);

                foreach (var kvp in _windowTaskPanes.Values)
                {
                    try { kvp.Control.ApplyLocalization(); } catch { }
                }
                _ribbonUi?.Invalidate();

                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    "✓ UI language switched to English (Default). Click [A/က EN] anytime to switch back to Myanmar.",
                    "✓ UI ဘာသာစကားကို မြန်မာဘာသာ (Myanmar) သို့ ပြောင်းလဲပြီးပါပြီ။ [A/က မြန်မာ] ခလုတ်ကိုနှိပ်၍ အင်္ဂလိပ်သို့ ပြန်ပြောင်းနိုင်ပါသည်။"));
            });
        }

        private void WireTaskPaneControlEvents(ProofingTaskPaneControl ctrl)
        {
            ctrl.RequestFullDocumentScan += TriggerManualDocumentScanWithHotReload;
            ctrl.RequestCancelScan += () =>
            {
                _bulkWriteCancelRequested = true;
                _documentScanner?.CancelUserScan();
            };
            ctrl.RequestToggleLanguage += ToggleUiLanguageFromUi;
            ctrl.RequestCycleEcoMode += CycleEcoModeFromUi;
            ctrl.RequestFixAllIssues += ExecuteFixAllInActiveDocument;
            ctrl.RequestClearUnderlines += () =>
            {
                if (_wordApp?.ActiveDocument != null)
                {
                    _underlineManager?.StripAllProofingUnderlines(_wordApp.ActiveDocument);
                    _documentScanner?.ClearCachedIssues(_wordApp.ActiveDocument);
                }
            };
            ctrl.RequestLicenseDialog += ShowLicenseDialog;
            ctrl.RequestSelectIssueInDocument += item => _underlineManager?.SelectAndScrollToIssue(item);
            ctrl.RequestApplySuggestion += (item, replacement) =>
            {
                if (_wordApp?.ActiveDocument != null)
                {
                    _underlineManager?.ReplaceIssueInDocument(_wordApp.ActiveDocument, item, replacement);
                    _documentScanner?.InvalidateParagraphCache(_wordApp.ActiveDocument);
                }
            };
            ctrl.RequestApplyBatchSuggestion += (originalText, replacementText) =>
            {
                Word.Document? doc = _wordApp?.ActiveDocument;
                if (doc != null && _documentScanner != null && _underlineManager != null)
                {
                    var cached = _documentScanner.GetCachedIssues(doc);
                    int replaced = _underlineManager.ReplaceMatchingIssuesInDocument(doc, cached, originalText, replacementText);
                    _documentScanner.InvalidateParagraphCache(doc);
                    ctrl.RenderIssues(_documentScanner.GetCachedIssues(doc));
                    if (replaced > 0)
                    {
                        ctrl.ShowToastBanner(UiLocalizer.Pick(
                            $"✓ Replaced all ({replaced}) matching occurrences with \"{replacementText}\" (press Ctrl+Z to undo).",
                            $"✓ တူညီသောအမှား ({replaced}) ခုလုံးကို \"{replacementText}\" ဖြင့် တစ်ပြိုင်နက် ပြင်ဆင်ပြီးပါပြီ (Ctrl+Z ပြန်ဆုတ်နိုင်သည်)။"));
                    }
                }
            };
            ctrl.RequestIgnoreOnce += item =>
            {
                _proofingEngine?.IgnoreWordInSession(item.Issue.OriginalText);
                item.IsResolved = true;
                if (item.TargetRange != null)
                {
                    try
                    {
                        item.TargetRange.Font.Underline = item.OriginalUnderline;
                        item.TargetRange.Font.UnderlineColor = item.OriginalUnderlineColor;
                    }
                    catch { }
                }
            };
            ctrl.RequestAddToDictionary += item =>
            {
                _proofingEngine?.AddCustomWord(item.Issue.OriginalText, _customDictFilePath);
                string normAdded = UnicodeNormalizer.NormalizeCanonicalOrder(item.Issue.OriginalText.Trim());
                if (_wordApp?.ActiveDocument != null && _documentScanner != null)
                {
                    foreach (var cached in _documentScanner.GetCachedIssues(_wordApp.ActiveDocument))
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                        {
                            cached.IsResolved = true;
                            if (cached.TargetRange != null)
                            {
                                try
                                {
                                    cached.TargetRange.Font.Underline = cached.OriginalUnderline;
                                    cached.TargetRange.Font.UnderlineColor = cached.OriginalUnderlineColor;
                                }
                                catch { }
                            }
                        }
                    }
                    ctrl.RenderIssues(_documentScanner.GetCachedIssues(_wordApp.ActiveDocument));
                }
            };
            ctrl.RequestDictionarySearch += query =>
                _proofingEngine?.SearchDictionary(query, 45) ?? new List<DictionaryEntry>();
            ctrl.RequestIsDictionaryIndexReady += () =>
                _proofingEngine?.IsDictionarySearchReady ?? true;
            ctrl.RequestSearchReferenceCorpus += (query, incProverbs, incEncyc) =>
                _proofingEngine?.SearchReferenceCorpus(query, incProverbs, incEncyc, 40) ?? new List<DictionaryEntry>();
            ctrl.RequestGetSynonyms += word =>
                _proofingEngine?.GetSynonyms(word, 8) ?? new List<string>();
            ctrl.RequestGetAntonyms += word =>
                _proofingEngine?.GetAntonyms(word, 8) ?? new List<string>();
            ctrl.RequestGetCollocations += word =>
                _proofingEngine?.GetTopCollocations(word, 6) ?? new List<string>();
            ctrl.RequestGetBookmarkedEntries += words =>
                _proofingEngine?.LookupBookmarkedEntries(words) ?? new List<DictionaryEntry>();
            ctrl.RequestGetFeaturedKnowledge += () =>
                _proofingEngine?.GetFeaturedKnowledgeEntries(8) ?? new List<DictionaryEntry>();
            ctrl.RequestGetSelectedDocumentText += () =>
            {
                try
                {
                    Word.Selection? sel = _wordApp?.Selection;
                    if (sel == null || sel.Range == null || sel.Range.Start == sel.Range.End)
                        return string.Empty;

                    string raw = (sel.Range.Text ?? string.Empty).Trim(' ', '\t', '\r', '\n', '\a', '၊', '။', '"', '\'', '“', '”', '(', ')');
                    return raw.Length <= 80 ? raw : raw.Substring(0, 80).Trim();
                }
                catch
                {
                    return string.Empty;
                }
            };
            ctrl.RequestHomophoneSummary += word =>
                _proofingEngine?.GetHomophoneSiblingsSummary(word) ?? string.Empty;
            ctrl.RequestChangeStyleMode += ApplyStyleModeAndRescan;
            ctrl.RequestInsertWordAtCursor += word =>
            {
                try
                {
                    if (_wordApp?.Selection != null)
                    {
                        _wordApp.Selection.TypeText(word);
                    }
                }
                catch { }
            };
            ctrl.RequestGetCustomWords += () =>
                _proofingEngine?.GetCustomWords() ?? Array.Empty<string>();
            ctrl.RequestAddCustomWordManual += word =>
            {
                if (_proofingEngine == null || !_proofingEngine.AddCustomWord(word, _customDictFilePath))
                    return false;

                string normAdded = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(word.Trim()));
                if (_wordApp?.ActiveDocument != null && _documentScanner != null)
                {
                    foreach (var cached in _documentScanner.GetCachedIssues(_wordApp.ActiveDocument))
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                        {
                            cached.IsResolved = true;
                            if (cached.TargetRange != null)
                            {
                                try
                                {
                                    cached.TargetRange.Font.Underline = cached.OriginalUnderline;
                                    cached.TargetRange.Font.UnderlineColor = cached.OriginalUnderlineColor;
                                }
                                catch { }
                            }
                        }
                    }
                    ctrl.RenderIssues(_documentScanner.GetCachedIssues(_wordApp.ActiveDocument));
                }
                return true;
            };
            ctrl.RequestRemoveCustomWord += word =>
            {
                if (_proofingEngine == null || !_proofingEngine.RemoveCustomWord(word, _customDictFilePath))
                    return false;

                if (_wordApp?.ActiveDocument != null && _documentScanner != null)
                {
                    _documentScanner.InvalidateParagraphCache(_wordApp.ActiveDocument);
                    _documentScanner.ScanFullDocumentAsync();
                }
                return true;
            };

            // Flagship V11/V12 Document Toolkit & Official Ministry Suite Events
            ctrl.RequestRewriteToOfficialStyle += selectionOnly => ExecuteToolkitRewriteOfficialProse(selectionOnly);
            ctrl.RequestConvertLegacyFonts += selectionOnly => ExecuteToolkitConvertFonts(selectionOnly);
            ctrl.RequestFormatNumerals += (toMyanmarDigits, selectionOnly) => ExecuteToolkitFormatNumerals(toMyanmarDigits, selectionOnly);
            ctrl.RequestFormatPunctuation += selectionOnly => ExecuteToolkitFormatPunctuation(selectionOnly);
            ctrl.RequestInsertOfficialTemplate += (kind, dt) => ExecuteInsertOfficialTemplate(kind, dt);
            ctrl.RequestRefreshQualityReport += () => BuildActiveDocumentQualityReport();
            ctrl.RequestExportAuditCertificate += ExecuteExportAuditCertificate;
            ctrl.RequestValidateOfficialLetter += () =>
            {
                string plain = _toolkitService.ExtractFullDocumentPlainText(_wordApp?.ActiveDocument);
                return OfficialLetterSuite.ValidateOfficialLetterStructure(plain);
            };
        }

        private DocumentQualityReport BuildActiveDocumentQualityReport()
        {
            Word.Document? doc = _wordApp?.ActiveDocument;
            if (doc != null && _documentScanner != null)
            {
                var cachedReport = _documentScanner.GetCachedQualityReport(doc);
                if (cachedReport != null)
                    return cachedReport;
            }

            var cached = (doc != null && _documentScanner != null)
                ? _documentScanner.GetCachedIssues(doc)
                : null;
            return BuildDocumentQualityReport(doc, cached);
        }

        private DocumentQualityReport BuildDocumentQualityReport(Word.Document? doc, IReadOnlyList<DocumentProofingItem>? items)
        {
            string plain = _toolkitService.ExtractFullDocumentPlainText(doc);
            IReadOnlyList<ProofingIssue>? unresolvedIssues = items != null
                ? items.Where(i => !i.IsResolved).Select(i => i.Issue).ToList()
                : null;
            return DocumentQualityAnalyzer.Analyze(plain, unresolvedIssues, _proofingEngine);
        }

        private void ExecuteToolkitConvertFonts(bool selectionOnly)
        {
            if (_wordApp == null) return;
            Word.Document? doc = _wordApp.ActiveDocument;
            if (doc != null) _underlineManager?.StripAllProofingUnderlines(doc);

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            _bulkWriteCancelRequested = false;
            ToolkitActionSummary res;
            try
            {
                res = _toolkitService.ConvertLegacyFontsInDocument(
                    _wordApp,
                    doc,
                    selectionOnly,
                    (pct, msg) => ctrl?.UpdateScanProgress(pct, msg),
                    () => _bulkWriteCancelRequested);
            }
            finally
            {
                ctrl?.SetScanningState(false);
            }

            if (doc != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(doc);
                _documentScanner.ScanFullDocumentAsync(allowSmartPartialAutoScan: true);
            }

            ctrl?.ShowToastBanner(res.UserMessage, res.Success);
        }

        private void ExecuteToolkitFormatNumerals(bool toMyanmarDigits, bool selectionOnly)
        {
            if (_wordApp == null) return;
            Word.Document? doc = _wordApp.ActiveDocument;
            if (doc != null) _underlineManager?.StripAllProofingUnderlines(doc);

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            _bulkWriteCancelRequested = false;
            ToolkitActionSummary res;
            try
            {
                res = _toolkitService.FormatNumeralsInDocument(
                    _wordApp,
                    doc,
                    toMyanmarDigits,
                    selectionOnly,
                    (pct, msg) => ctrl?.UpdateScanProgress(pct, msg),
                    () => _bulkWriteCancelRequested);
            }
            finally
            {
                ctrl?.SetScanningState(false);
            }

            if (doc != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(doc);
                _documentScanner.ScanFullDocumentAsync(allowSmartPartialAutoScan: true);
            }

            ctrl?.ShowToastBanner(res.UserMessage, res.Success);
        }

        private void ExecuteToolkitFormatPunctuation(bool selectionOnly)
        {
            if (_wordApp == null) return;
            Word.Document? doc = _wordApp.ActiveDocument;
            if (doc != null) _underlineManager?.StripAllProofingUnderlines(doc);

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            _bulkWriteCancelRequested = false;
            ToolkitActionSummary res;
            try
            {
                res = _toolkitService.FormatOfficialPunctuationInDocument(
                    _wordApp,
                    doc,
                    selectionOnly,
                    (pct, msg) => ctrl?.UpdateScanProgress(pct, msg),
                    () => _bulkWriteCancelRequested);
            }
            finally
            {
                ctrl?.SetScanningState(false);
            }

            if (doc != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(doc);
                _documentScanner.ScanFullDocumentAsync(allowSmartPartialAutoScan: true);
            }

            ctrl?.ShowToastBanner(res.UserMessage, res.Success);
        }

        private void ExecuteToolkitRewriteOfficialProse(bool selectionOnly)
        {
            if (_wordApp == null) return;
            Word.Document? doc = _wordApp.ActiveDocument;
            if (doc != null) _underlineManager?.StripAllProofingUnderlines(doc);

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            _bulkWriteCancelRequested = false;
            int changed;
            try
            {
                changed = _toolkitService.RewriteProseToOfficialStyle(
                    _wordApp,
                    doc,
                    selectionOnly,
                    (pct, msg) => ctrl?.UpdateScanProgress(pct, msg),
                    () => _bulkWriteCancelRequested);
            }
            finally
            {
                ctrl?.SetScanningState(false);
            }

            if (doc != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(doc);
                _documentScanner.ScanFullDocumentAsync(allowSmartPartialAutoScan: true);
            }

            string msg = changed > 0
                ? UiLocalizer.Pick(
                    $"✓ Rewrote ({changed}) colloquial expressions into formal/official Burmese style (press Ctrl+Z to undo).",
                    $"✓ စကားပြောဟန် အသုံးအနှုန်း ({changed}) နေရာကို ရုံးသုံး/စာပေဟန်သို့ တစ်ချက်တည်းဖြင့် ပြောင်းလဲပြီးပါပြီ (Ctrl+Z ဖြင့် ပြန်ပြင်နိုင်သည်)။")
                : UiLocalizer.Pick(
                    "✓ No colloquial expressions found — document is already in formal/official style.",
                    "✓ ပြောင်းလဲရန် စကားပြောဟန် အသုံးအနှုန်း မတွေ့ရှိပါ (ရုံးသုံးစာပေဟန် အသင့်ဖြစ်ပြီးဖြစ်သည်)။");
            ctrl?.ShowToastBanner(msg, isSuccess: true);
        }

        private void ExecuteInsertOfficialTemplate(OfficialTemplateKind kind, DateTime referenceDate)
        {
            if (_wordApp == null) return;
            _toolkitService.InsertOfficialTemplateAtCursor(_wordApp, kind, referenceDate);
            if (_wordApp.ActiveDocument != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(_wordApp.ActiveDocument);
                _documentScanner.ScanFullDocumentAsync();
            }

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.ShowToastBanner(UiLocalizer.Pick(
                "✓ Inserted official letter template in Pyidaungsu font.",
                "✓ ရုံးသုံး စံပုံစံကြမ်းကို Pyidaungsu ဖောင့်ဖြင့် ထည့်သွင်းပြီးပါပြီ။"));
        }

        private void ExecuteExportAuditCertificate()
        {
            if (_wordApp == null) return;
            Word.Document? sourceDoc = _wordApp.ActiveDocument;
            DocumentQualityReport report = BuildActiveDocumentQualityReport();
            bool ok = _toolkitService.GeneratePrintableAuditCertificateDocument(_wordApp, sourceDoc, report);
            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.ShowToastBanner(
                ok
                    ? UiLocalizer.Pick(
                        "✓ Generated Myanmar Document Quality Audit Report in a new Word document.",
                        "✓ မြန်မာစာ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (Word စာတမ်းအသစ်) ထုတ်ပေးပြီးပါပြီ။")
                    : UiLocalizer.Pick(
                        "⚠ Could not generate the audit report document.",
                        "⚠ အစီရင်ခံစာ ထုတ်ယူစဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။"),
                ok);
        }

        private void ExecuteFixAllInActiveDocument(IssueFilterMode filterMode = IssueFilterMode.All)
        {
            Word.Document? doc = _wordApp?.ActiveDocument;
            if (doc == null || _documentScanner == null || _underlineManager == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);

            Func<DocumentProofingItem, bool> predicate = filterMode switch
            {
                IssueFilterMode.SpellingOnly => i => !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning,
                IssueFilterMode.GrammarOnly => i => i.Issue.IsGrammarOrStyle,
                IssueFilterMode.ContextOnly => i => i.Issue.IsContextualWarning,
                _ => _ => true
            };

            string scopeLabel = filterMode switch
            {
                IssueFilterMode.SpellingOnly => UiLocalizer.Pick("Spelling Issues", "သတ်ပုံအမှား"),
                IssueFilterMode.GrammarOnly => UiLocalizer.Pick("Grammar & Style Issues", "သဒ္ဒါ/စာဟန်အမှား"),
                IssueFilterMode.ContextOnly => UiLocalizer.Pick("Context Meaning Warnings", "အနက်သံသယ စိစစ်ချက်"),
                _ => UiLocalizer.Pick("All Issues", "အမှားစုစုပေါင်း")
            };

            var cached = _documentScanner.GetCachedIssues(doc);
            var candidates = cached
                .Where(i => !i.IsResolved &&
                            i.Issue.Suggestions != null &&
                            i.Issue.Suggestions.Count > 0 &&
                            predicate(i))
                .ToList();

            if (candidates.Count == 0)
            {
                ctrl?.ShowToastBanner(UiLocalizer.Pick(
                    "✓ No remaining issues to fix.",
                    "✓ ပြင်ဆင်ရန် အမှားအယွင်း မကျန်ရှိတော့ပါ။"));
                return;
            }

            var previewEntries = candidates
                .Select(item => new FixPreviewEntry<DocumentProofingItem>(
                    item,
                    UiLocalizer.Pick(
                        $"{item.StoryName} • Para #{item.ParagraphIndex}",
                        $"{item.StoryName} • ပိုဒ် #{item.ParagraphIndex}"),
                    TryExtractWordParagraphSnippet(item),
                    item.Issue))
                .ToList();

            using (var dlg = new FixAllPreviewDialog<DocumentProofingItem>(
                previewEntries,
                $"Word — {scopeLabel}",
                target => _underlineManager.SelectAndScrollToIssue(target)))
            {
                IWin32Window? ownerWin = ctrl != null && ctrl.IsHandleCreated ? ctrl : null;
                DialogResult result = ownerWin != null ? dlg.ShowDialog(ownerWin) : dlg.ShowDialog();
                if (result != DialogResult.OK || dlg.ApprovedReplacements.Count == 0)
                {
                    return;
                }

                int skippedCount = candidates.Count - dlg.ApprovedReplacements.Count;
                _bulkWriteCancelRequested = false;
                int count;
                try
                {
                    count = _underlineManager.ReplaceApprovedIssuesInDocument(
                        doc,
                        cached,
                        dlg.ApprovedReplacements,
                        (pct, msg) => ctrl?.UpdateScanProgress(pct, msg),
                        () => _bulkWriteCancelRequested);
                }
                finally
                {
                    ctrl?.SetScanningState(false);
                }
                _documentScanner.InvalidateParagraphCache(doc);

                ctrl?.RenderIssues(_documentScanner.GetCachedIssues(doc));

                if (count > 0)
                {
                    string skipTail = skippedCount > 0
                        ? UiLocalizer.Pick($" ({skippedCount} skipped)", $" (ချန်လှပ်ထားသည် {skippedCount} ခု)")
                        : string.Empty;
                    ctrl?.ShowToastBanner(UiLocalizer.Pick(
                        $"✓ Applied ({count}) approved fixes{skipTail} (press Ctrl+Z to undo).",
                        $"✓ စိစစ်အတည်ပြုထားသော ({count}) ခုကို တစ်ပြိုင်နက် ပြင်ဆင်ပြီးပါပြီ{skipTail} (Ctrl+Z ဖြင့် မူလအတိုင်း ပြန်ပြင်နိုင်သည်)။"));
                }
            }
        }

        private static string TryExtractWordParagraphSnippet(DocumentProofingItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.ParagraphSnippet))
                return item.ParagraphSnippet;

            try
            {
                if (item.TargetRange != null && item.TargetRange.Paragraphs.Count > 0)
                {
                    string raw = item.TargetRange.Paragraphs[1].Range.Text ?? string.Empty;
                    string cleaned = raw.Replace("\r", " ").Replace("\a", " ").Replace("\n", " ").Trim();
                    if (!string.IsNullOrEmpty(cleaned))
                    {
                        item.ParagraphSnippet = cleaned;
                        return cleaned;
                    }
                }
            }
            catch { }

            return item.Issue.OriginalText ?? string.Empty;
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
            catch
            {
                // Ignore
            }
            return ProofingStyleMode.GeneralStandard;
        }

        private void ApplyStyleModeAndRescan(ProofingStyleMode mode)
        {
            _pendingStyleMode = mode;
            try
            {
                if (!string.IsNullOrEmpty(_styleModeConfigPath))
                {
                    File.WriteAllText(_styleModeConfigPath, mode.ToString());
                }
            }
            catch
            {
                // Ignore
            }

            foreach (var kv in _windowTaskPanes.Values)
            {
                kv.Control.SetStyleMode(mode);
            }
            _ribbonUi?.InvalidateControl("BtnToggleFormalStyle");

            if (_proofingEngine == null)
                return;

            _proofingEngine.StyleMode = mode;

            if (_wordApp?.ActiveDocument != null)
            {
                _underlineManager?.StripAllProofingUnderlines(_wordApp.ActiveDocument);
                _documentScanner?.InvalidateParagraphCache(_wordApp.ActiveDocument);
                _documentScanner?.ScanFullDocumentAsync();
            }
        }

        public string GetCustomUI(string ribbonId)
        {
            if (!_isPrimaryInstance)
                return string.Empty;

            return @"
<customUI xmlns='http://schemas.microsoft.com/office/2009/07/customui' onLoad='OnRibbonLoad'>
  <ribbon>
    <tabs>
      <tab id='TabMyanmarProofing' getLabel='GetRibbonLabel'>
        <group id='GrpProofingActions' getLabel='GetRibbonLabel'>
          <button id='BtnScanFullDocument'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='SpellingAndGrammar'
                  onAction='OnRibbonScanDocument'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnFixAllIssues'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='AcceptInvitation'
                  onAction='OnRibbonFixAllIssues'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnClearUnderlines'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='ClearFormatting'
                  onAction='OnRibbonClearUnderlines'
                  getScreentip='GetRibbonScreentip' />
        </group>
        <group id='GrpStyleAndLive' getLabel='GetRibbonLabel'>
          <toggleButton id='BtnToggleFormalStyle'
                        getLabel='GetRibbonLabel'
                        size='large'
                        imageMso='FileDocumentInspect'
                        getPressed='GetFormalStylePressed'
                        onAction='OnRibbonToggleFormalStyle'
                        getScreentip='GetRibbonScreentip' />
          <toggleButton id='BtnToggleLiveScan'
                        getLabel='GetRibbonLabel'
                        size='large'
                        imageMso='ReviewTrackChanges'
                        getPressed='GetLiveScanPressed'
                        onAction='OnRibbonToggleLiveScan'
                        getScreentip='GetRibbonScreentip' />
        </group>
        <group id='GrpDictionaryAndTools' getLabel='GetRibbonLabel'>
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
          <button id='BtnToggleUiLanguage'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='Translate'
                  onAction='OnRibbonToggleLanguage'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnManageCustomWords'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='BookmarkInsert'
                  onAction='OnRibbonManageCustomWords'
                  getScreentip='GetRibbonScreentip' />
          <button id='BtnLicenseActivation'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='Info'
                  onAction='OnRibbonOpenLicense'
                  getScreentip='GetRibbonScreentip' />
        </group>
        <group id='GrpTypographyToolkit' getLabel='GetRibbonLabel'>
          <button id='BtnConvertFonts'
                  getLabel='GetRibbonLabel'
                  size='large'
                  imageMso='FontDialog'
                  onAction='OnRibbonConvertFonts'
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
        <group id='GrpOfficialSuite' getLabel='GetRibbonLabel'>
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
          <button id='BtnInsertOfficialLetter'
                  getLabel='GetRibbonLabel'
                  size='normal'
                  imageMso='FileNewDefault'
                  onAction='OnRibbonInsertOfficialLetter'
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
            return control?.Id switch
            {
                "TabMyanmarProofing" => UiLocalizer.Pick("Myanmar Proofing", "မြန်မာစာ စစ်ဆေးရေး"),
                "GrpProofingActions" => UiLocalizer.Pick("Proofing & Fixes", "စာတမ်း စစ်ဆေးရေးနှင့် ပြင်ဆင်မှု"),
                "BtnScanFullDocument" => UiLocalizer.Pick("Scan Document", "စာတမ်းတစ်ခုလုံး စစ်ဆေးမည်"),
                "BtnFixAllIssues" => UiLocalizer.Pick("Fix All (Preview)", "အားလုံး တစ်ပြိုင်နက် ပြင်မည်"),
                "BtnClearUnderlines" => UiLocalizer.Pick("Clear Marks", "မျဉ်းလှိုင်းများ ဖယ်ရှားမည်"),
                "GrpStyleAndLive" => UiLocalizer.Pick("Style & Auto-Scan", "အရေးအသားနှင့် အလိုအလျောက်စနစ်"),
                "BtnToggleFormalStyle" => UiLocalizer.Pick("Formal / Official Style", "စာပေ / ရုံးသုံး အရေးစကားစနစ်"),
                "BtnToggleLiveScan" => UiLocalizer.Pick("Live Auto-Check", "အလိုအလျောက် စစ်ဆေးမှု (Live)"),
                "GrpDictionaryAndTools" => UiLocalizer.Pick("Dictionary & Workspace", "အဘိဓာန်နှင့် စစ်ဆေးရေးပြကွက် (Offline)"),
                "BtnSearchDictionary" => UiLocalizer.Pick("Myanmar Dictionary", "သတ်ပုံကျမ်းနှင့် အဘိဓာန် ရှာရန်"),
                "BtnToggleTaskPane" => UiLocalizer.Pick("Show/Hide Pane", "စစ်ဆေးရေးပြကွက် ပြ/ဖျောက်"),
                "BtnToggleUiLanguage" => UiLocalizer.Pick("UI: English (⇄ မြန်မာ)", "UI: မြန်မာ (⇄ EN)"),
                "BtnManageCustomWords" => UiLocalizer.Pick("Custom Words", "မှတ်သားဝေါဟာရ စီမံရန်"),
                "BtnLicenseActivation" => UiLocalizer.Pick("About & GPLv3 License", "ဖန်တီးသူနှင့် လိုင်စင် (GPLv3)"),
                "GrpTypographyToolkit" => UiLocalizer.Pick("Font & Numerals", "ဖောင့်နှင့် ကိန်းဂဏန်း ကိရိယာ"),
                "BtnConvertFonts" => "WinInnwa/Zawgyi ➔ Pyidaungsu",
                "BtnNumeralsToMyanmar" => "0-9 ➔ ၀-၉ (Smart)",
                "BtnCleanPunctuation" => UiLocalizer.Pick("Clean Punctuation ၊ / ။", "ပုဒ်ဖြတ် ၊ / ။ ရှင်းမည်"),
                "GrpOfficialSuite" => UiLocalizer.Pick("Official Suite & Audit", "ရုံးစာနှင့် အရည်အသွေး စစ်ဆေးချက်"),
                "BtnOpenToolkitPane" => UiLocalizer.Pick("Toolkit & Calendar", "ကိရိယာ နှင့် ပြက္ခဒိန်"),
                "BtnRewriteOfficialProse" => UiLocalizer.Pick("Rewrite Formal Style", "ရုံးသုံးစာပေဟန် ပြောင်းမည်"),
                "BtnInsertOfficialLetter" => UiLocalizer.Pick("Insert Official Letter", "ရုံးစာ ပုံစံကြမ်း ထည့်ရန်"),
                "BtnExportAuditReport" => UiLocalizer.Pick("Quality Audit Report", "အရည်အသွေး အစီရင်ခံစာ"),
                _ => string.Empty
            };
        }

        public string GetRibbonScreentip(Office.IRibbonControl control)
        {
            return control?.Id switch
            {
                "BtnScanFullDocument" => UiLocalizer.Pick(
                    "Scan document body, tables, headers, and footers for Myanmar spelling, grammar, and meaning issues",
                    "စာကိုယ်၊ ဇယားကွက်နှင့် ခေါင်းစီး/အောက်ခြေ အားလုံးကို စစ်ဆေးရန်"),
                "BtnFixAllIssues" => UiLocalizer.Pick(
                    "Open Safe Fix-All Preview Checklist before applying replacements (Single Ctrl+Z)",
                    "တွေ့ရှိသော အမှားအားလုံးကို သတ်ပုံကျမ်းအမှန်ဖြင့် တစ်ပြိုင်နက် အစားထိုးရန် (Single Ctrl+Z)"),
                "BtnClearUnderlines" => UiLocalizer.Pick(
                    "Clear temporary proofing wavy underlines from the document",
                    "စာတမ်းပေါ်ရှိ စစ်ဆေးရေး မျဉ်းလှိုင်းများကို ဖယ်ရှားရန်"),
                "BtnToggleFormalStyle" => UiLocalizer.Pick(
                    "Off = General Standard spelling; On = also check colloquial-to-formal style (ပါတယ် -> ပါသည်, တွေ -> များ)",
                    "ပိတ်ထားပါက အထွေထွေ စံသတ်ပုံစနစ်ဖြင့် စစ်ဆေးပြီး၊ ဖွင့်ထားပါက စာပေနှင့် ရုံးသုံးအရေးစကား (ပါတယ်->ပါသည်၊ တွေ->များ) ပါ စစ်ဆေးပေးပါမည်"),
                "BtnToggleLiveScan" => UiLocalizer.Pick(
                    "Enable or pause real-time proofing while typing",
                    "စာရိုက်နေစဉ် အလိုအလျောက် စစ်ဆေးမှုကို ဖွင့်/ပိတ်ရန်"),
                "BtnSearchDictionary" => UiLocalizer.Pick(
                    "Search official Myanmar orthography, definitions, proverbs, and encyclopedia offline",
                    "သတ်ပုံကျမ်း၊ မြန်မာအဘိဓာန် နှင့် စကားပုံများကို အမြန်ရှာဖွေရန်"),
                "BtnToggleTaskPane" => UiLocalizer.Pick(
                    "Show or hide the Myanmar Spell Checker Task Pane on the right",
                    "ညာဘက် စစ်ဆေးရေးပြကွက် (Task Pane) ကို ဖွင့်/ပိတ် ပြုလုပ်ရန်"),
                "BtnToggleUiLanguage" => UiLocalizer.Pick(
                    "Switch user interface language between English (Default) and Myanmar (မြန်မာ) instantly",
                    "UI ဘာသာစကားကို အင်္ဂလိပ် (English) နှင့် မြန်မာ (Myanmar) အကြား ချက်ချင်းပြောင်းလဲရန်"),
                "BtnManageCustomWords" => UiLocalizer.Pick(
                    "Add or remove personal and organization custom Myanmar words",
                    "ကိုယ်ပိုင် / ရုံးသုံး မှတ်သားထားသော စာလုံးများကို ထည့်သွင်း/ဖျက်သိမ်းရန်"),
                "BtnLicenseActivation" => UiLocalizer.Pick(
                    "View system info, creator Htet Yar Zar (Kyaukse University), and GNU GPL v3.0 open-source license",
                    "စနစ်အချက်အလက်၊ ဖန်တီးသူ ထက်ရာဇာ (ကျောက်ဆည်တက္ကသိုလ်) နှင့် GNU GPL v3.0 Open-Source လိုင်စင် ကြည့်ရှုရန်"),
                "BtnConvertFonts" => UiLocalizer.Pick(
                    "Convert WinInnwa, WinResearcher, CE, and Zawgyi text to Pyidaungsu Unicode without breaking English fonts",
                    "WinInnwa, WinResearcher, CE နှင့် Zawgyi ဖောင့်များကို အင်္ဂလိပ်ဖောင့်နှင့် ပုံစံမပျက်စေဘဲ Pyidaungsu သို့ တစ်ချက်နှိပ် ပြောင်းလဲရန်"),
                "BtnNumeralsToMyanmar" => UiLocalizer.Pick(
                    "Smart convert 0-9 to Myanmar digits ၀-၉ while protecting URLs, emails, ISO-9001, A4, 5G",
                    "URL, Email, COVID-19, ISO-9001, A4, 5G တို့ကို မထိခိုက်ဘဲ မြန်မာဂဏန်း ပြောင်းလဲရန်"),
                "BtnCleanPunctuation" => UiLocalizer.Pick(
                    "Normalize Myanmar comma (၊), full stop (။) spacing, and strip stray ZWSP characters",
                    "ပုဒ်ထီး (၊)၊ ပုဒ်မ (။) အကွာအဝေးနှင့် မမြင်ရသော ZWSP များကို သန့်စင်ညှိနှိုင်းရန်"),
                "BtnOpenToolkitPane" => UiLocalizer.Pick(
                    "Open Kyat amount converter, Myanmar calendar, and official letter tools",
                    "ကျပ်ငွေ (သိန်း) စာသားပြောင်းစက်၊ မြန်မာသက္ကရာဇ် ပြက္ခဒိန် နှင့် ရုံးစာပြကွက် ဖွင့်ရန်"),
                "BtnRewriteOfficialProse" => UiLocalizer.Pick(
                    "Convert colloquial expressions into formal/official Burmese prose in one click (Single Ctrl+Z)",
                    "စကားပြောဟန် (ပါတယ်၊ တွေ၊ နဲ့၊ ဒီနေ့...) များကို အစိုးရရုံးသုံး စာပေအရေးအသားသို့ တစ်ချက်နှိပ် ပြောင်းလဲရန် (Single Ctrl+Z)"),
                "BtnInsertOfficialLetter" => UiLocalizer.Pick(
                    "Insert an official Burmese letter template with Myanmar calendar date at cursor",
                    "မြန်မာသက္ကရာဇ် ရက်စွဲပါဝင်သော ရုံးစာ ပုံစံကြမ်းကို လက်ရှိနေရာ၌ ထည့်သွင်းရန်"),
                "BtnExportAuditReport" => UiLocalizer.Pick(
                    "Generate a printable Myanmar Document Quality Audit Report in Word",
                    "စာတမ်း၏ စာလုံးပေါင်းနှင့် ရုံးသုံးစံနှုန်း အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (Word) ထုတ်ယူရန်"),
                _ => string.Empty
            };
        }

        public void OnRibbonToggleLanguage(Office.IRibbonControl control)
        {
            ToggleUiLanguageFromUi();
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
            return _documentScanner?.IsLiveAutoScanEnabled ?? _pendingLiveAutoScan;
        }

        public void OnRibbonToggleLiveScan(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleLiveScan", () =>
            {
                _pendingLiveAutoScan = pressed;
                if (_documentScanner != null)
                {
                    _documentScanner.IsLiveAutoScanEnabled = pressed;
                }
                _ribbonUi?.InvalidateControl("BtnToggleLiveScan");
            });
        }

        public bool GetTaskPanePressed(Office.IRibbonControl control)
        {
            if (_wordApp == null)
                return false;

            int hwnd = 0;
            try { hwnd = _wordApp.ActiveWindow?.Hwnd ?? 0; } catch { }
            return hwnd != 0 && _windowTaskPanes.TryGetValue(hwnd, out var entry) && entry.Pane.Visible;
        }

        public void OnRibbonScanDocument(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonScanDocument", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                TriggerManualDocumentScanWithHotReload();
            });
        }

        private void TriggerManualDocumentScanWithHotReload()
        {
            if (_proofingEngine == null || _documentScanner == null)
                return;

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

                var activeCtrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
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

                            if (_wordApp?.ActiveDocument != null)
                            {
                                _documentScanner.InvalidateParagraphCache(_wordApp.ActiveDocument);
                            }
                            _documentScanner.ScanFullDocumentAsync();
                        });
                    }
                    catch
                    {
                        SafeBeginInvokeUi(_uiMarshaller, () => _documentScanner?.ScanFullDocumentAsync());
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _dbHotReloadInProgress, 0);
                    }
                });
                return;
            }

            if (_wordApp?.ActiveDocument != null)
            {
                _documentScanner.InvalidateParagraphCache(_wordApp.ActiveDocument);
            }
            _documentScanner.ScanFullDocumentAsync();
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

        public void OnRibbonFixAllIssues(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonFixAllIssues", () => ExecuteFixAllInActiveDocument(IssueFilterMode.All));
        }

        public void OnRibbonSearchDictionary(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonSearchDictionary", () =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                ctrl?.FocusDictionarySearchBox();
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

        public void OnRibbonToggleTaskPane(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleTaskPane", () =>
            {
                if (_wordApp == null) return;
                int hwnd = 0;
                try { hwnd = _wordApp.ActiveWindow?.Hwnd ?? 0; } catch { }

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
            });
        }

        public void OnRibbonClearUnderlines(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonClearUnderlines", () =>
            {
                if (_wordApp?.ActiveDocument != null)
                {
                    _underlineManager?.StripAllProofingUnderlines(_wordApp.ActiveDocument);
                    _documentScanner?.ClearCachedIssues(_wordApp.ActiveDocument);
                    var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                    ctrl?.ShowToastBanner(UiLocalizer.Pick(
                        "✓ Cleared temporary proofing underlines.",
                        "စစ်ဆေးရေး မျဉ်းလှိုင်းများကို ဖယ်ရှားပြီးပါပြီ။"));
                }
            });
        }

        public void OnRibbonOpenLicense(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonOpenLicense", ShowLicenseDialog);
        }

        public void OnRibbonConvertFonts(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonConvertFonts", () => ExecuteToolkitConvertFonts(selectionOnly: false));
        }

        public void OnRibbonNumeralsToMyanmar(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonNumeralsToMyanmar", () => ExecuteToolkitFormatNumerals(toMyanmarDigits: true, selectionOnly: false));
        }

        public void OnRibbonCleanPunctuation(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonCleanPunctuation", () => ExecuteToolkitFormatPunctuation(selectionOnly: false));
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
                bool selOnly = false;
                try
                {
                    Word.Selection? sel = _wordApp?.Selection;
                    if (sel?.Range != null && sel.Range.Start != sel.Range.End)
                        selOnly = true;
                }
                catch { }
                ExecuteToolkitRewriteOfficialProse(selectionOnly: selOnly);
            });
        }

        public void OnRibbonInsertOfficialLetter(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonInsertOfficialLetter", () =>
            {
                ExecuteInsertOfficialTemplate(OfficialTemplateKind.MinistryOfficialLetter, DateTime.Today);
            });
        }

        public void OnRibbonExportAuditReport(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonExportAuditReport", ExecuteExportAuditCertificate);
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

                foreach (int h in _paneWidthTimers.Keys.ToList())
                {
                    StopAndRemovePaneWidthTimer(h);
                }
                _settledPaneWidthHwnds.Clear();

                if (_wordApp != null)
                {
                    _wordApp.WindowSelectionChange -= OnWindowSelectionChange;
                    _wordApp.WindowBeforeRightClick -= OnWindowBeforeRightClick;
                    _wordApp.WindowActivate -= OnWindowActivate;
                    _wordApp.WindowDeactivate -= OnWindowDeactivate;
                    _wordApp.DocumentOpen -= OnDocumentOpen;
                    ((Word.ApplicationEvents4_Event)_wordApp).NewDocument -= OnNewDocument;
                    _wordApp.DocumentBeforeSave -= OnDocumentBeforeSave;
                    _wordApp.DocumentBeforePrint -= OnDocumentBeforePrint;
                    _wordApp.DocumentBeforeClose -= OnDocumentBeforeClose;
                }
                _contextMenuHandler?.CloseActivePopup();
                _contextMenuHandler?.ClearCustomContextMenuItems();
                _documentScanner?.Dispose();
                _underlineManager?.Dispose();
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
